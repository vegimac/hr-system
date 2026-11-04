using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace HrSystem.Services.Elm;

/// <summary>
/// Versicherungsprofil bestellen (SubscribeOrganization, Order-…-Profile). Bestellt werden darf nur das
/// aktuelle oder das vergangene Jahr (System Test ST_ELM_V6_06_02). Rein, ohne DB/Netz.
/// </summary>
public static class ElmProfilBestellung
{
    private static readonly XNamespace Sd = "urn:ch:swissdec:elm:v6:20260306:salarydeclaration";
    private static readonly XNamespace Ep = "urn:ch:swissdec:basis:v1:20260306:components";

    /// <summary>Fehlermeldung, oder null, wenn die AccountingPeriod bestellt werden darf.</summary>
    public static string? Pruefe(string? roh, DateTime heute)
    {
        var text = (roh ?? "").Trim();
        if (!Regex.IsMatch(text, @"^[0-9]{4}$"))
            return $"Das Geschäftsjahr «{text}» ist ungültig: Es braucht genau vier Ziffern. Es wurde nichts gesendet.";
        var jahr = int.Parse(text, CultureInfo.InvariantCulture);
        return jahr == heute.Year || jahr == heute.Year - 1
            ? null
            : $"Das Geschäftsjahr {jahr} ist ungültig: Bestellt werden kann nur {heute.Year} oder {heute.Year - 1}. Es wurde nichts gesendet.";
    }

    /// <summary>Elementname der Bestellung. Nur diese drei Versicherungen liefern Profile.</summary>
    public static string ElementName(string? domain) => domain switch
    {
        "UVG-LAA" => "Order-UVG-LAA-Profile",
        "UVGZ-LAAC" => "Order-UVGZ-LAAC-Profile",
        "KTG-AMC" => "Order-KTG-AMC-Profile",
        _ => throw new InvalidOperationException("Ein Profil gibt es nur bei UVG, UVGZ und KTG."),
    };

    public static XElement BaueStory(string? domain, string jahr, DateTime jetzt, string storyId)
        => new(Sd + ElementName(domain),
            new XElement(Ep + "Creation", jetzt.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture)),
            new XElement(Ep + "StoryID", storyId),
            new XElement(Sd + "AccountingPeriod", jahr));

    public static string StoryId(XElement story) => story.Element(Ep + "StoryID")!.Value;
}
