using System.Xml.Linq;
using static HrSystem.Services.Elm.ElmGemeinsam;

namespace HrSystem.Services.Elm;

/// <summary>
/// Rektifikat eines einzelnen Lohnausweises (RL Kapitel 9): der Lohnausweis wie in der Jahresmeldung, neue DocID,
/// in den Standardbemerkungen Verweis auf DocID und Datum des fehlerhaften Originals. Geht nur an die
/// Steuerverwaltung. Rein: baut aus der fertigen Jahresmeldung, abgeglichen mit RefXML_2025-01_RECTIFICATE.xml.
/// </summary>
public static class ElmRektifikat
{
    public sealed record Angaben(string PersonalNr, string? OriginalDocId, DateOnly? OriginalDatum, string? Bemerkung);

    public sealed record Original(string DocId, DateOnly Datum);

    private static XElement? Person(XElement root, string personalNr) => root.Descendants(Sd + "Person")
        .FirstOrDefault(p => p.Descendants(C + "EmployeeNumber").FirstOrDefault()?.Value.Trim() == personalNr.Trim());

    /// <summary>DocID und Datum des Lohnausweises dieser Person in einer gesendeten Meldung. Null = keiner darin.</summary>
    public static Original? FindeOriginal(string meldungXml, string personalNr)
    {
        var tax = Person(XDocument.Parse(meldungXml).Root!, personalNr)?.Descendants(Sd + "TaxSalary").FirstOrDefault();
        var id = tax?.Element(Sd + "DocID")?.Value.Trim();
        var datum = tax?.Element(Sd + "CreationDate")?.Value.Trim();
        if (string.IsNullOrEmpty(id) || datum is not { Length: >= 10 }
            || !DateOnly.TryParse(datum[..10], System.Globalization.CultureInfo.InvariantCulture, out var d)) return null;
        return new Original(id, d);
    }

    /// <summary>Aus der Jahresmeldung: nur diese Person, nur ihr Lohnausweis, nur ihre Steuerverwaltung.</summary>
    public static XElement Baue(XElement jahresmeldung, string personalNr, Original original, string? bemerkung, DateTime jetzt)
    {
        var root = new XElement(jahresmeldung);
        var person = Person(root, personalNr)
            ?? throw new InvalidOperationException($"Personalnummer {personalNr} steht nicht in der Jahresmeldung dieses Jahres.");
        var tax = person.Descendants(Sd + "TaxSalary").FirstOrDefault()
            ?? throw new InvalidOperationException($"Personalnummer {personalNr} hat in diesem Jahr keinen Lohnausweis.");
        var staff = person.Parent!;
        staff.Elements(Sd + "Person").Where(p => p != person).Remove();
        person.Elements().Where(e => e.Name != C + "Particulars" && e.Name != C + "Work" && e.Name != Sd + "TaxSalaries").Remove();

        tax.Element(Sd + "DocID")!.Value = Guid.NewGuid().ToString();
        tax.Element(Sd + "CreationDate")!.Value = jetzt.ToString("yyyy-MM-ddTHH:mm:ss.fff");
        tax.Element(Sd + "Remark")?.Remove();
        var bemerkungen = tax.Element(Sd + "StandardRemark");
        if (bemerkungen == null) tax.Add(bemerkungen = new XElement(Sd + "StandardRemark"));
        bemerkungen.Add(new XElement(Sd + "Rectificate",
            new XElement(Sd + "OriginalDate", original.Datum.ToString("yyyy-MM-dd")),
            new XElement(Sd + "OriginalDocID", original.DocId)));
        if (!string.IsNullOrWhiteSpace(bemerkung)) tax.Add(new XElement(Sd + "Remark", bemerkung.Trim()));

        var adressat = (string?)tax.Attribute("addresseeIDRef") ?? "";
        root.Element(Sdc + "Job")!.Element(Sdc + "Addressees")!.Elements(Sdc + "Addressee")
            .Where(a => (string?)a.Attribute("addresseeID") != adressat).Remove();
        var meldung = root.Descendants(Sd + "AnnualSalaryDeclaration").First();
        meldung.Element(Sd + "Institutions")!.Elements()
            .Where(i => (string?)i.Attribute("addresseeIDRef") != adressat).Remove();
        meldung.Element(Sd + "SalaryTotals")!.RemoveNodes();
        var zaehler = meldung.Element(Sd + "SalaryCounters")!;
        zaehler.RemoveNodes();
        zaehler.Add(new XElement(Sd + "NumberOf-TaxSalary-Tags", 1));
        return root;
    }
}
