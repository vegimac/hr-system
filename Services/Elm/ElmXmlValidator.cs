using System.Xml;
using System.Xml.Schema;

namespace HrSystem.Services.Elm;

/// <summary>
/// XSD-Validierung gegen die ELM-6.0-Schemas (Etappe E2, Walter 27.08.2026).
/// Schemas liegen als Kopie in Assets/Swissdec/ (Quelle:
/// docs/swissdec/Transmitter_Richtlinien/schema). Es werden NUR die fünf
/// Service-Schemas geladen — die noNS-Tax-Schemas (Lohnausweis-Barcode,
/// Anhang 5) würden mit leerem Namespace kollidieren.
/// </summary>
public class ElmXmlValidator
{
    private static readonly string[] SchemaFiles =
    {
        "SwissdecComponents.xsd",
        "Common.xsd",
        "SalaryDeclaration.xsd",
        "SalaryDeclarationContainer.xsd",
        "SalaryDeclarationServiceTypes.xsd"
    };

    private static readonly Lazy<XmlSchemaSet> _schemas = new(() =>
    {
        var set = new XmlSchemaSet();
        var dir = Path.Combine(AppContext.BaseDirectory, "Assets", "Swissdec");
        foreach (var f in SchemaFiles)
            set.Add(null, Path.Combine(dir, f));
        set.Compile();
        return set;
    });

    /// <summary>Validiert das XML; leere Liste = valid.</summary>
    public List<string> Validate(string xml)
    {
        var fehler = new List<string>();
        try
        {
            var settings = new XmlReaderSettings
            {
                ValidationType = ValidationType.Schema,
                Schemas = _schemas.Value
            };
            settings.ValidationFlags |= XmlSchemaValidationFlags.ReportValidationWarnings;
            settings.ValidationEventHandler += (_, e) =>
            {
                var pos = e.Exception != null ? $" (Zeile {e.Exception.LineNumber})" : "";
                fehler.Add($"{e.Severity}: {e.Message}{pos}");
            };
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            while (reader.Read()) { /* nur durchlesen — Events sammeln */ }
        }
        catch (Exception ex)
        {
            fehler.Add("Validator-Fehler: " + ex.GetBaseException().Message);
        }
        return fehler;
    }
}
