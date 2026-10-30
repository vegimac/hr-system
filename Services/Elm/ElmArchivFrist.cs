namespace HrSystem.Services.Elm;

/// <summary>
/// Wie lange die signierten ELM-Archivdateien liegen bleiben (Walter 30.10.2026).
///
/// Swissdec schreibt nur «unverschlüsselt archivieren, gemäss eigenen Vorgaben»
/// (SUA-Detailspezifikation) und nennt keine Jahreszahl. Massgebend ist deshalb
/// Art. 958f OR: Geschäftsbücher und Buchungsbelege zehn Jahre, die Frist beginnt
/// mit dem Ablauf des Geschäftsjahres. Geschäftsjahr ist hier das Kalenderjahr.
/// Eine Datei aus 2026 bleibt bis 31.12.2036 und darf ab 01.01.2037 weg.
/// </summary>
public static class ElmArchivFrist
{
    public const int Jahre = 10;

    public static DateTime? ZeitAusName(string? name)
    {
        if (name == null || name.Length < 8) return null;
        return DateTime.TryParseExact(name.AsSpan(0, 8), "yyyyMMdd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var tag)
            ? tag : null;
    }

    /// <summary>Letzter Tag, an dem die Datei noch liegen muss.</summary>
    public static DateTime AufbewahrenBis(DateTime dateiTag)
        => new(dateiTag.Year + Jahre, 12, 31);

    public static bool DarfWeg(DateTime dateiTag, DateTime heute)
        => heute.Date > AufbewahrenBis(dateiTag);
}
