namespace HrSystem.Services;

// ============================================================================
// Welche Dateien OneCrew beim Hochladen speichert (Walter-Vorgabe 01.10.2026):
// nur noch PDF und Bilder. Word, Excel, PowerPoint, ZIP & Co. werden abgelehnt
// mit dem Hinweis, sie als PDF umzuwandeln. Word/Excel kann OneCrew selbst
// umwandeln (OfficeToPdfService) — dafür liefert Fehler() das Flag umwandelbar.
//
// Gilt für alle Dokument-Ablagen (MA-Dokumente, Posteingang/Mitteilungen,
// Filial-Dokumente, Kandidaten). NICHT für Importer (Excel/CSV/ZIP als
// Datenquelle, z.B. d.velop-Import) und nicht für E-Mail-Anhänge.
// ============================================================================
public static class DokumentUploadRegel
{
    public static readonly string[] ErlaubteEndungen =
        { ".pdf", ".jpg", ".jpeg", ".png", ".gif", ".tif", ".tiff", ".heic", ".heif", ".webp" };

    public const string Hinweis =
        "Word-, Excel- und ZIP-Dateien werden nicht mehr gespeichert. Bitte als PDF umwandeln und speichern.";

    public static bool IstErlaubt(string? dateiname)
    {
        var ext = Path.GetExtension(dateiname ?? "").ToLowerInvariant();
        return Array.IndexOf(ErlaubteEndungen, ext) >= 0;
    }

    /// <summary>Antwort-Objekt für 400 — gleiche Form in allen Controllern.</summary>
    public static object Fehler(string? dateiname)
    {
        var ext = Path.GetExtension(dateiname ?? "").ToLowerInvariant();
        return new
        {
            error = "NUR_PDF",
            message = $"«{Path.GetFileName(dateiname ?? "")}»: {Hinweis}",
            endung = ext,
            umwandelbar = OfficeToPdfService.CanConvert(dateiname),
        };
    }
}
