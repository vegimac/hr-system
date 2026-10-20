using iText.Forms;
using iText.IO.Font.Constants;
using iText.Kernel.Font;
using iText.Kernel.Pdf;

namespace HrSystem.Services.Meldepflicht;

/// <summary>Füllt das offizielle SEM-Formular 2b (Ausweis B/S/F).</summary>
public static class Formular2bPdf
{
    public static byte[] Fuellen(byte[] vorlage, Formular2bDaten d)
    {
        using var input = new MemoryStream(vorlage);
        using var output = new MemoryStream();
        using var pdf = new PdfDocument(new PdfReader(input), new PdfWriter(output));
        var form = PdfAcroForm.GetAcroForm(pdf, true);

        Text(form, "NrSymic", d.Zemis);
        Text(form, "Noms", d.Name);
        Text(form, "Prénoms", d.Vorname);
        Text(form, "Geburtsdatum", d.Geburtsdatum);
        Text(form, "Nationalité", d.Nationalitaet);
        Text(form, "Numéro de téléphone", d.Telefon);
        Text(form, "Rue", d.Strasse);
        Text(form, "NPALocalité", d.PlzOrt);

        if (d.Weiblich == true) Radio(form, "Group3", "Auswahl4");
        else if (d.Weiblich == false) Radio(form, "Group3", "Auswahl3");

        Text(form, "NomB", d.Arbeitgeber);
        Text(form, "RueB", d.ArbeitgeberStrasse);
        Text(form, "NPALocaliteB", d.ArbeitgeberPlzOrt);
        Text(form, "NuméroIDEB", d.Uid);
        Text(form, "Nom2", d.KontaktName);
        Text(form, "Tel2", d.KontaktTelefon);
        Text(form, "Courriel2", d.KontaktEmail);
        Text(form, "Courriel1", d.KontaktEmail);

        Text(form, "ActiviteExercee", d.Taetigkeit);
        Text(form, "BrancheEconomique", "Gastronomie");
        Text(form, "Lieu", d.Arbeitsort);
        Text(form, "cantons", d.Kanton);
        if (!string.IsNullOrWhiteSpace(d.Kanton))
            Text(form, "cbxVille", d.Kanton.Trim().ToUpperInvariant());

        Radio(form, "Group4", "Auswahl5"); // L-GAV: Ja

        if (d.Stundenlohn)
        {
            Radio(form, "Group5", "Auswahl11");
            Text(form, "Bruttolohn", d.Lohn);
        }
        else
        {
            Radio(form, "Group5", "Auswahl9"); // monatlich, 13 Monatslöhne
            Text(form, "Bruttolohn", d.Lohn);
        }
        Text(form, "Beschaeftigunsgrad", d.Pensum);
        Text(form, "Stunden", d.Wochenstunden);
        Text(form, "Minuten", d.Wochenminuten);

        if (d.Beendigung)
        {
            Check(form, "ChkBxEndTaetigkeit");
            Text(form, "DtEndTaetigkeit", d.Datum);
            Radio(form, "GroupEndTaetigkeit", "Unselbstaedige");
        }
        else
        {
            Check(form, "ChkBxStartTaetigkeit");
            Text(form, "DtStartTaetigkeit", d.Datum);
            Radio(form, "GroupStartTaetigkeit", "Unselbstaedige");
        }

        form.SetNeedAppearances(true);
        pdf.Close();
        return output.ToArray();
    }

    static void Text(PdfAcroForm form, string feld, string? wert)
    {
        if (string.IsNullOrWhiteSpace(wert)) return;
        var field = form.GetField(feld);
        if (field == null) return;
        field.SetFont(PdfFontFactory.CreateFont(StandardFonts.HELVETICA));
        field.SetValue(wert.Trim());
    }

    static void Radio(PdfAcroForm form, string feld, string zustand)
        => form.GetField(feld)?.SetValue(zustand);

    static void Check(PdfAcroForm form, string feld)
    {
        var field = form.GetField(feld);
        if (field == null) return;
        var an = field.GetAppearanceStates()?
            .FirstOrDefault(z => !string.Equals(z, "Off", StringComparison.OrdinalIgnoreCase));
        field.SetValue(an ?? "Yes");
    }
}

public sealed class Formular2bDaten
{
    public string? Zemis { get; init; }
    public string? Name { get; init; }
    public string? Vorname { get; init; }
    public string? Geburtsdatum { get; init; }
    public string? Nationalitaet { get; init; }
    public string? Telefon { get; init; }
    public string? Strasse { get; init; }
    public string? PlzOrt { get; init; }
    /// <summary>true = weiblich (rechtes Kästchen), false = männlich, null = offen.</summary>
    public bool? Weiblich { get; init; }
    public string? Arbeitgeber { get; init; }
    public string? ArbeitgeberStrasse { get; init; }
    public string? ArbeitgeberPlzOrt { get; init; }
    public string? Uid { get; init; }
    public string? KontaktName { get; init; }
    public string? KontaktTelefon { get; init; }
    public string? KontaktEmail { get; init; }
    public string? Taetigkeit { get; init; }
    public string? Arbeitsort { get; init; }
    public string? Kanton { get; init; }
    public bool Stundenlohn { get; init; }
    public string? Lohn { get; init; }
    public string? Pensum { get; init; }
    public string? Wochenstunden { get; init; }
    public string? Wochenminuten { get; init; }
    public bool Beendigung { get; init; }
    public string? Datum { get; init; }
}
