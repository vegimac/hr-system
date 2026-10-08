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

    /// <summary>
    /// Endung UND Inhalt prüfen (Walter-Vorgabe 08.10.2026): eine umbenannte Datei
    /// («rechnung.exe» → «rechnung.pdf») wird abgelehnt. Liefert das Antwort-Objekt
    /// für 400 oder null, wenn die Datei gespeichert werden darf. Leere Datei → null
    /// (meldet der Aufrufer selbst).
    /// </summary>
    public static object? Pruefe(IFormFile? file)
    {
        if (file == null || file.Length == 0) return null;
        if (!IstErlaubt(file.FileName)) return Fehler(file.FileName);
        return InhaltPasst(file.FileName, Kopf(file)) ? null : InhaltFehler(file.FileName);
    }

    /// <summary>Nur Inhalt (PDF oder Bild), Endung egal — für das MA-Postfach, wo Handy-Uploads oft ohne passende Endung kommen.</summary>
    public static bool IstPdfOderBild(IFormFile file)
    {
        var k = Kopf(file);
        return IstPdf(k) || IstBild(k);
    }

    public const int KopfLaenge = 1024;

    private static byte[] Kopf(IFormFile file)
    {
        var puffer = new byte[KopfLaenge];
        int n;
        using (var s = file.OpenReadStream()) n = KopfLesen(s, puffer);
        return puffer[..n];
    }

    /// <summary>
    /// .pdf → muss ein PDF sein («%PDF-» in den ersten 1024 Bytes, wie Acrobat es verlangt).
    /// Bild-Endung → irgendein erlaubtes Bildformat (eine als .jpg gespeicherte PNG ist harmlos
    /// und kommt bei Handys/Webseiten oft vor).
    /// </summary>
    public static bool InhaltPasst(string? dateiname, ReadOnlySpan<byte> kopf)
    {
        var ext = Path.GetExtension(dateiname ?? "").ToLowerInvariant();
        if (Array.IndexOf(ErlaubteEndungen, ext) < 0) return false;
        return ext == ".pdf" ? IstPdf(kopf) : IstBild(kopf);
    }

    public static object InhaltFehler(string? dateiname)
    {
        var name = Path.GetFileName(dateiname ?? "");
        var ext = Path.GetExtension(name).ToLowerInvariant();
        var art = ext == ".pdf" ? "kein echtes PDF" : "kein echtes Bild";
        return new
        {
            error = "INHALT_PASST_NICHT",
            message = $"«{name}» ist {art} — der Inhalt passt nicht zur Endung {ext}. Bitte die Originaldatei hochladen.",
            endung = ext,
        };
    }

    private static int KopfLesen(Stream s, byte[] puffer)
    {
        int gesamt = 0, n;
        while (gesamt < puffer.Length && (n = s.Read(puffer, gesamt, puffer.Length - gesamt)) > 0)
            gesamt += n;
        return gesamt;
    }

    private static bool IstPdf(ReadOnlySpan<byte> k) => k.IndexOf("%PDF-"u8) >= 0;

    private static readonly string[] HeifMarken =
        { "heic", "heix", "hevc", "hevx", "heim", "heis", "hevm", "hevs", "mif1", "msf1", "avif", "avis" };

    private static bool IstBild(ReadOnlySpan<byte> k)
    {
        if (k.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF })) return true;                                   // JPEG
        if (k.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return true;     // PNG
        if (k.StartsWith("GIF87a"u8) || k.StartsWith("GIF89a"u8)) return true;                           // GIF
        if (k.StartsWith(new byte[] { 0x49, 0x49, 0x2A, 0x00 }) || k.StartsWith(new byte[] { 0x4D, 0x4D, 0x00, 0x2A })) return true; // TIFF
        if (k.Length >= 12 && k.StartsWith("RIFF"u8) && k.Slice(8, 4).SequenceEqual("WEBP"u8)) return true; // WebP
        return IstHeif(k);
    }

    // HEIC/HEIF (iPhone): ISO-Box «ftyp» mit Haupt- oder kompatibler Marke aus HeifMarken.
    // Andere ftyp-Dateien (MP4-Videos: isom, mp42 …) gelten nicht als Bild.
    private static bool IstHeif(ReadOnlySpan<byte> k)
    {
        if (k.Length < 16 || !k.Slice(4, 4).SequenceEqual("ftyp"u8)) return false;
        var boxLaenge = (k[0] << 24) | (k[1] << 16) | (k[2] << 8) | k[3];
        var ende = Math.Min(Math.Max(boxLaenge, 16), k.Length);
        if (Marke(k.Slice(8, 4))) return true;
        for (var i = 16; i + 4 <= ende; i += 4)
            if (Marke(k.Slice(i, 4))) return true;
        return false;
    }

    private static bool Marke(ReadOnlySpan<byte> b)
    {
        Span<char> c = stackalloc char[4];
        for (var i = 0; i < 4; i++) c[i] = (char)b[i];
        foreach (var m in HeifMarken)
            if (c.SequenceEqual(m)) return true;
        return false;
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
