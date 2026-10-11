using iText.Forms;
using iText.Kernel.Pdf;

namespace HrSystem.Services;

/// <summary>
/// Füllt das GastroSocial-Formular FOR 0402-dt «Anmeldung für Familien-/
/// Geburtszulagen» (Walter 11.10.2026, docs/famz-anmeldung-konzept.md).
/// Das PDF hat echte Formularfelder (AcroForm) — Namen und Ankreuz-Zustände
/// sind unten fest zugeordnet und wurden am Template geprüft. Ankreuzfelder
/// tragen als «an»-Zustand durchgehend «weiblich» (Eigenheit des Formulars),
/// Optionsgruppen «1»/«2»/«3» bzw. benannte Zustände. Alle Werte kommen als
/// bereits editierter DTO aus dem Frontend.
/// </summary>
public class FamzAnmeldungPdfService
{
    private readonly IWebHostEnvironment _env;
    public FamzAnmeldungPdfService(IWebHostEnvironment env) => _env = env;

    public const string TemplateFile = "FamzAnmeldung_GastroSocial_0402.pdf";
    public const int MaxKinder = 5;

    private string TemplatePath() =>
        System.IO.Path.Combine(_env.ContentRootPath, "Assets", "Forms", TemplateFile);

    public byte[] Generate(FamzAnmeldungDto d)
    {
        var templatePath = TemplatePath();
        if (!System.IO.File.Exists(templatePath))
            throw new FileNotFoundException(
                $"Template fehlt: {templatePath}. Bitte «{TemplateFile}» in Assets/Forms/ ablegen.");

        using var reader = new PdfReader(templatePath);
        using var ms = new MemoryStream();
        using var writer = new PdfWriter(ms);
        using (var pdf = new PdfDocument(reader, writer))
        {
            var form = PdfAcroForm.GetAcroForm(pdf, true);
            form.SetGenerateAppearance(true);
            Fuelle(form, d);
            try { form.FlattenFields(); } catch { /* kein Showstopper */ }
        }
        return ms.ToArray();
    }

    // ── Feldzuordnung ────────────────────────────────────────────────────

    static void Fuelle(PdfAcroForm f, FamzAnmeldungDto d)
    {
        // Seite 1 — Antrag
        Radio(f, "Ich_beantrage", (d.Beantrage ?? "").ToUpperInvariant() switch
        {
            "FAMILIENZULAGE" => "Arbeitnehmer",
            "GEBURTSZULAGE" => "SE",
            "DIFFERENZZULAGE" => "NE",
            _ => null,
        });
        Radio(f, "Ich_bin", (d.Status ?? "").ToUpperInvariant() switch
        {
            "ARBEITNEHMER" => "Arbeitnehmer",
            "SE" => "SE",
            "NE" => "NE",
            _ => null,
        });

        // Antragstellende Person
        var a = d.Antragsteller ?? new FamzPersonDto();
        Person(f, a, "", "Geschlecht");
        Text(f, "Erreichbar", d.Erreichbar);
        Text(f, "Ab wann", d.AbWann);
        Radio(f, "Leistung", JaNein12(d.LeistungBezug, jaIst2: true));
        Text(f, "Leistung von", d.LeistungBezug == true ? d.LeistungVon : null);

        // Arbeitgeber
        Text(f, "Arbeitgeber Name Betrieb", d.AgName);
        Text(f, "Abrechnungsnummer_AK", d.AgAbrechnungsnummer);
        Text(f, "Arbeitgeber Adresse", d.AgStrasse);
        Text(f, "Arbeitgeber_PLZ_Ort", d.AgPlzOrt);
        Text(f, "Arbeitgeber Beschäftigt von", d.BeschaeftigtVon);
        Text(f, "Arbeitgeber Beschäftigt bis", d.BeschaeftigtBis);
        Text(f, "ArbeitgeberArbeitspesum", d.Arbeitspensum);
        Text(f, "Arbeitgeber Kanton", d.ArbeitsortKanton);
        Text(f, "Arbeitgeber Bruttolohn", d.Bruttolohn);
        Radio(f, "Arbeitgeber Erwerbseinkommen", JaNein12(d.EinkommenUeberMinimum, jaIst2: false));
        Radio(f, "Arbeitgeber_Arbeitgebende", JaNein12(d.WeitereArbeitgeber, jaIst2: false));

        // Seite 2 — weitere Arbeitgebende
        Text(f, "Weiter_Arbeitgebende_1", d.WeitereArbeitgeber == true ? d.WeitereAg1 : null);
        Text(f, "Weiter_Arbeitgebende_2", d.WeitereArbeitgeber == true ? d.WeitereAg2 : null);
        Text(f, "Bei_welchem_Arbeitgeber_1", d.WeitereArbeitgeber == true ? d.HoeheresEinkommenBei : null);

        // Anderer Elternteil (Seite 2/3)
        if (d.AndererElternteil is { } e)
        {
            Person(f, e, "_2_1", "Geschlecht_2_1");
            Radio(f, "Anstellungsverhaeltnis_2.1", JaNein12(e.Angestellt, jaIst2: true));
            if (e.Angestellt == true)
            {
                Text(f, "Ja. Name Zeile 2 3.1", e.ArbeitgeberZeile1);
                Text(f, "Ja. Name Zeile 2 2.1", e.ArbeitgeberZeile2);
                Text(f, "Kanton 2.1", e.ArbeitsortKanton);
                Text(f, "Arbeitspensum 2.1", e.Arbeitspensum);
            }
            if (e.EinkommenHoeher == true) Check(f, "Elternteil Ja 2.1");
            else if (e.EinkommenHoeher == false) Check(f, "Elternteil Nein 2.1");
            Radio(f, "Erwerbseinkommen höher 2.1", JaNein12(e.EinkommenUeberMinimum, jaIst2: false));
            var se = (e.SeNe ?? "").ToUpperInvariant();
            Radio(f, "Erwerbseinkommen 2.1", se switch { "NEIN" => "1", "SE" or "NE" => "2", _ => null });
            if (se == "SE") Check(f, "Elternteil SE 2.1");
            if (se == "NE") Check(f, "Elternteil NE 2.1");
            if (se is "SE" or "NE") Text(f, "Zuständige Ausgleichskasse 2.1", e.Ausgleichskasse);
            Radio(f, "Leistung Elternteil 2.1", JaNein12(e.LeistungBezug, jaIst2: true));
            if (e.LeistungBezug == true)
            {
                Text(f, "Leistung von 2.1", e.LeistungVon1);
                Text(f, "Leistung von 2.11", e.LeistungVon2);
            }
        }

        // Aktueller Ehepartner (Seite 3) — nur wenn nicht der andere Elternteil
        if (d.Partner is { } p)
        {
            Person(f, p, "_2_2", "Geschlecht_2_2");
            Radio(f, "Anstellungsverhaeltnis_2.2", JaNein12(p.Angestellt, jaIst2: true));
            if (p.Angestellt == true)
            {
                Text(f, "Ja. Name 2.2", p.ArbeitgeberZeile1);
                Text(f, "Ja. Name Zeile 2 2.2", p.ArbeitgeberZeile2);
                Text(f, "Kanton 3.1", p.ArbeitsortKanton);
                Text(f, "Arbeitspensum 3.1", p.Arbeitspensum);
            }
            Radio(f, "Erwerbseinkommen 3.1", JaNein12(p.EinkommenHoeher, jaIst2: false));
            Radio(f, "Erwerbseinkommen höher 3.1", JaNein12(p.EinkommenUeberMinimum, jaIst2: false));
            var se = (p.SeNe ?? "").ToUpperInvariant();
            if (se == "NEIN") Check(f, "Elternteil Nein 3.1");
            if (se is "SE" or "NE") Check(f, "Elternteil Ja 3.1");
            if (se == "SE") Check(f, "Elternteil SE 3.1");
            if (se == "NE") Check(f, "Elternteil NE 3.1");
            if (se is "SE" or "NE") Text(f, "Zuständige Ausgleichskasse 3.1", p.Ausgleichskasse);
            Radio(f, "Leistung 3.1", JaNein12(p.LeistungBezug, jaIst2: true));
            if (p.LeistungBezug == true)
            {
                Text(f, "Leistung von 3.1", p.LeistungVon1);
                Text(f, "Leistung von 3.2", p.LeistungVon2);
            }
        }

        // Seite 4 — Kinder
        var kinder = (d.Kinder ?? new()).Take(MaxKinder).ToList();
        for (int i = 0; i < kinder.Count; i++)
        {
            var k = kinder[i];
            int n = i + 1;
            Text(f, $"Allgemeine Angaben Kind {n}", k.Name);
            Text(f, $"Allgemeine Angaben Vorname Kind {n}", k.Vorname);
            Text(f, $"Allgemeine Angaben Geburtsdatum Kind {n}", k.Geburtsdatum);
            var g = (k.Geschlecht ?? "").ToUpperInvariant();
            if (g == "M") Check(f, $"Allg. Angaben Geschlecht M {n}");
            if (g is "F" or "W") Check(f, $"Allg. Angaben Geschlecht F {n}");
            var bez = (k.Beziehung ?? "").ToUpperInvariant();
            if (bez is "L" or "A" or "S" or "P" or "G" or "E")
                Check(f, bez == "L" && n == 3 ? "Beziehung L3" : $"Beziehung {bez} {n}");
            if (k.Erwerbsunfaehig) Check(f, $"Erwerbsunfähig {n}");
            Text(f, $"Zusaetzliche_Angaben_{n}", k.Wohnadresse);
        }
        Radio(f, "Elterliche_Sorge_zu", ElternMutterVater(d.Sorge));
        Text(f, "Elterliche_Sorge_Bemerkung_1", d.SorgeBemerkung);
        Radio(f, "Elterliche_Sorge_zu_2", ElternMutterVater(d.LebenBei));
        Text(f, "Elterliche_Sorge_Bemerkung_2", d.LebenBeiBemerkung);

        // Seite 5
        Text(f, "Bemerkung 1", d.Bemerkung1);
        Text(f, "Bemerkung2", d.Bemerkung2);
        Text(f, "Bemerkung3", d.Bemerkung3);
        Text(f, "Ort und Datum 4.1", d.OrtDatumAntragsteller);
        Text(f, "Ort und Datum 4.2", d.OrtDatumArbeitgeber);
    }

    static readonly (string Code, string Feld)[] ZivilFelder =
    {
        ("ledig", "Ledig"),
        ("verheiratet", "Verheiratet"),
        ("getrennt", "Getrennt"),
        ("geschieden", "Geschieden"),
        ("verwitwet", "Verwitwet"),
        ("eingetragene_partnerschaft", "Eingetragene Partnerschaft"),
        ("aufgeloeste_partnerschaft", "Aufgelöste Partnerschaft"),
    };

    static void Person(PdfAcroForm f, FamzPersonDto p, string suffix, string geschlechtFeld)
    {
        Text(f, "Name_Vorname" + suffix, p.NameVorname);
        Text(f, "Strasse_Nummer" + suffix, p.StrasseNr);
        Text(f, "PLZ_Ort" + suffix, p.PlzOrt);
        Text(f, "Kanton" + suffix, p.Kanton);
        Text(f, "Geburtsdatum" + suffix, p.Geburtsdatum);
        Text(f, "AHV-Nummer" + suffix, OhneLaendercode(p.AhvNummer));
        var g = (p.Geschlecht ?? "").ToUpperInvariant();
        Radio(f, geschlechtFeld, g == "M" ? "1" : g is "F" or "W" ? "2" : null);
        var zs = (p.Zivilstand ?? "").Trim().ToLowerInvariant();
        foreach (var (code, feld) in ZivilFelder)
            if (zs == code) Check(f, feld + suffix);
        Text(f, "Seit" + suffix, p.ZivilstandSeit);
        Text(f, "Nationalität" + suffix, p.Nationalitaet);
        Text(f, "Ausländerausweis" + suffix, p.Auslaenderausweis);
    }

    /// <summary>«756.1234.5678.97» → «1234567897»: 756 ist vorgedruckt, das Feld hat ein Kästchen pro Ziffer.</summary>
    public static string? OhneLaendercode(string? ahv)
    {
        var ziffern = new string((ahv ?? "").Where(char.IsDigit).ToArray());
        if (ziffern.Length == 0) return null;
        return ziffern.Length == 13 && ziffern.StartsWith("756") ? ziffern[3..] : ziffern;
    }

    /// <summary>Ja/Nein auf Optionsgruppe «1»/«2»; <paramref name="jaIst2"/> = «Nein» steht vorne.</summary>
    static string? JaNein12(bool? wert, bool jaIst2)
        => wert == null ? null : (wert.Value == jaIst2 ? "2" : "1");

    static string? ElternMutterVater(string? s) => (s ?? "").ToUpperInvariant() switch
    {
        "ELTERN" => "1",
        "MUTTER" => "2",
        "VATER" => "3",
        _ => null,
    };

    static void Text(PdfAcroForm f, string feld, string? wert)
    {
        if (string.IsNullOrWhiteSpace(wert)) return;
        var field = f.GetField(feld);
        field?.SetValue(wert.Trim());
    }

    static void Radio(PdfAcroForm f, string feld, string? zustand)
    {
        if (zustand == null) return;
        var field = f.GetField(feld);
        field?.SetValue(zustand);
    }

    static void Check(PdfAcroForm f, string feld)
    {
        var field = f.GetField(feld);
        if (field == null) return;
        string? an = null;
        try
        {
            an = field.GetAppearanceStates()?
                .FirstOrDefault(z => !string.Equals(z, "Off", StringComparison.OrdinalIgnoreCase));
        }
        catch { /* Aussehen nicht lesbar */ }
        field.SetValue(an ?? "weiblich");
    }
}

// ── DTO (Prefill-Antwort und PDF-Anfrage) ───────────────────────────────

public class FamzPersonDto
{
    public string? NameVorname { get; set; }
    public string? StrasseNr { get; set; }
    public string? PlzOrt { get; set; }
    public string? Kanton { get; set; }
    public string? Geburtsdatum { get; set; }
    public string? AhvNummer { get; set; }
    /// <summary>M | F</summary>
    public string? Geschlecht { get; set; }
    /// <summary>ledig | verheiratet | getrennt | geschieden | verwitwet | eingetragene_partnerschaft | aufgeloeste_partnerschaft</summary>
    public string? Zivilstand { get; set; }
    public string? ZivilstandSeit { get; set; }
    public string? Nationalitaet { get; set; }
    public string? Auslaenderausweis { get; set; }
}

public class FamzElternteilDto : FamzPersonDto
{
    public int? FamilyMemberId { get; set; }
    public bool? Angestellt { get; set; }
    public string? ArbeitgeberZeile1 { get; set; }
    public string? ArbeitgeberZeile2 { get; set; }
    public string? ArbeitsortKanton { get; set; }
    public string? Arbeitspensum { get; set; }
    /// <summary>Verdient diese Person mehr als der/die MA?</summary>
    public bool? EinkommenHoeher { get; set; }
    public bool? EinkommenUeberMinimum { get; set; }
    /// <summary>NEIN | SE | NE</summary>
    public string? SeNe { get; set; }
    public string? Ausgleichskasse { get; set; }
    public bool? LeistungBezug { get; set; }
    public string? LeistungVon1 { get; set; }
    public string? LeistungVon2 { get; set; }
}

public class FamzKindDto
{
    public int? FamilyMemberId { get; set; }
    public string? Name { get; set; }
    public string? Vorname { get; set; }
    public string? Geburtsdatum { get; set; }
    public string? Geschlecht { get; set; }
    /// <summary>L | A | S | P | G | E</summary>
    public string? Beziehung { get; set; }
    public bool Erwerbsunfaehig { get; set; }
    /// <summary>Nur für Kinder ausserhalb des Haushalts: Strasse, PLZ/Ort, Land.</summary>
    public string? Wohnadresse { get; set; }
}

public class FamzAnmeldungDto
{
    /// <summary>FAMILIENZULAGE | GEBURTSZULAGE | DIFFERENZZULAGE</summary>
    public string? Beantrage { get; set; } = "FAMILIENZULAGE";
    /// <summary>ARBEITNEHMER | SE | NE</summary>
    public string? Status { get; set; } = "ARBEITNEHMER";
    public FamzPersonDto? Antragsteller { get; set; }
    public string? Erreichbar { get; set; }
    public string? AbWann { get; set; }
    public bool? LeistungBezug { get; set; }
    public string? LeistungVon { get; set; }

    public string? AgName { get; set; }
    public string? AgAbrechnungsnummer { get; set; }
    public string? AgStrasse { get; set; }
    public string? AgPlzOrt { get; set; }
    public string? BeschaeftigtVon { get; set; }
    public string? BeschaeftigtBis { get; set; }
    public string? Arbeitspensum { get; set; }
    public string? ArbeitsortKanton { get; set; }
    public string? Bruttolohn { get; set; }
    public bool? EinkommenUeberMinimum { get; set; }
    public bool? WeitereArbeitgeber { get; set; }
    public string? WeitereAg1 { get; set; }
    public string? WeitereAg2 { get; set; }
    public string? HoeheresEinkommenBei { get; set; }

    public FamzElternteilDto? AndererElternteil { get; set; }
    public FamzElternteilDto? Partner { get; set; }

    public List<FamzKindDto>? Kinder { get; set; }
    /// <summary>ELTERN | MUTTER | VATER</summary>
    public string? Sorge { get; set; }
    public string? SorgeBemerkung { get; set; }
    public string? LebenBei { get; set; }
    public string? LebenBeiBemerkung { get; set; }

    public string? Bemerkung1 { get; set; }
    public string? Bemerkung2 { get; set; }
    public string? Bemerkung3 { get; set; }
    public string? OrtDatumAntragsteller { get; set; }
    public string? OrtDatumArbeitgeber { get; set; }
}
