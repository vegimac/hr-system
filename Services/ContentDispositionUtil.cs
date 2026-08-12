using Microsoft.Net.Http.Headers;

namespace HrSystem.Services;

/// <summary>
/// Content-Disposition-Header mit Umlaut-sicherem Dateinamen (Walter-Bug
/// 12.08.2026: «Özlem Eheman.pdf» → HTTP 500, weil Kestrel Nicht-ASCII in
/// rohen Header-Werten ablehnt). SetHttpFileName setzt gemäss RFC 5987
/// beides: <c>filename</c> (ASCII-Fallback) und <c>filename*</c> (UTF-8) —
/// Browser zeigen/speichern damit den Original-Namen inkl. Umlauten.
/// IMMER diesen Helper verwenden statt
/// <c>$"inline; filename=\"{name}\""</c> (roh = 500 bei Umlauten) oder
/// <c>Uri.EscapeDataString(name)</c> (kein 500, aber «%C3%96zlem…» als
/// Anzeigename).
/// </summary>
public static class ContentDispositionUtil
{
    /// <param name="dispositionType">"inline" oder "attachment"</param>
    /// <param name="fileName">Original-Dateiname (darf Umlaute enthalten, darf leer sein)</param>
    /// <param name="fallback">Name falls fileName leer, z.B. "dokument.pdf"</param>
    public static string Build(string dispositionType, string? fileName, string fallback)
    {
        var cd = new ContentDispositionHeaderValue(dispositionType);
        cd.SetHttpFileName(string.IsNullOrWhiteSpace(fileName) ? fallback : fileName);
        return cd.ToString();
    }
}
