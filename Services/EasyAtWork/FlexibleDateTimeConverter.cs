using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HrSystem.Services.EasyAtWork;

/// <summary>
/// Toleranter JsonConverter für DateTime/DateOnly — easy@work liefert je nach
/// Feld:
///   - "2026-06-17 16:04:45"     (Space statt T, ohne Zeitzone — beobachtet)
///   - "2026-06-17T16:04:45Z"    (ISO 8601 mit Z)
///   - "2026-06-17T16:04:45+02:00"
///   - "2026-06-17"              (nur Datum, für DateOnly-Felder)
/// Standardparser (System.Text.Json) akzeptiert nur das strikte ISO-8601-
/// „T"-Format → wir parsen permissiv.
/// </summary>
public class FlexibleDateTimeConverter : JsonConverter<DateTime?>
{
    private static readonly string[] Formats =
    {
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm:ss.fff",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-ddTHH:mm:ssZ",
        "yyyy-MM-ddTHH:mm:ss.fff",
        "yyyy-MM-ddTHH:mm:ss.fffZ",
        "yyyy-MM-ddTHH:mm:sszzz",
        "yyyy-MM-ddTHH:mm:ss.fffzzz",
        "yyyy-MM-dd",
    };

    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        var s = reader.GetString();
        if (string.IsNullOrWhiteSpace(s)) return null;

        if (DateTime.TryParse(s, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
            return DateTime.SpecifyKind(dt, DateTimeKind.Utc);

        if (DateTime.TryParseExact(s, Formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out dt))
            return DateTime.SpecifyKind(dt, DateTimeKind.Utc);

        return null; // wirft NICHT — easy@work-Felder können auch unverstanden sein
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value == null) writer.WriteNullValue();
        else writer.WriteStringValue(value.Value.ToString("o", CultureInfo.InvariantCulture));
    }
}

/// <summary>
/// String-Felder, die easy@work je nach Endpoint mal als JSON-String, mal als
/// Zahl liefert (z.B. <c>employee.number</c> kommt als 12345 ohne Quotes).
/// Wir akzeptieren beides und geben immer einen <see cref="string"/> zurück.
/// </summary>
public class FlexibleStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:   return null;
            case JsonTokenType.String: return reader.GetString();
            case JsonTokenType.Number:
                // Wir wollen "12345", nicht "12345.0". TryGetInt64 zuerst.
                if (reader.TryGetInt64(out var l)) return l.ToString(CultureInfo.InvariantCulture);
                if (reader.TryGetDouble(out var d)) return d.ToString(CultureInfo.InvariantCulture);
                return null;
            case JsonTokenType.True:   return "true";
            case JsonTokenType.False:  return "false";
            default:
                reader.Skip();
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value == null) writer.WriteNullValue();
        else writer.WriteStringValue(value);
    }
}

/// <summary>Pendant für DateOnly?-Felder (z.B. birth_date, from, to).</summary>
public class FlexibleDateOnlyConverter : JsonConverter<DateOnly?>
{
    public override DateOnly? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        var s = reader.GetString();
        if (string.IsNullOrWhiteSpace(s)) return null;

        if (DateOnly.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d;
        // Wenn da ein voller Timestamp kommt, das Datum extrahieren.
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
            return DateOnly.FromDateTime(dt);

        return null;
    }

    public override void Write(Utf8JsonWriter writer, DateOnly? value, JsonSerializerOptions options)
    {
        if (value == null) writer.WriteNullValue();
        else writer.WriteStringValue(value.Value.ToString("yyyy-MM-dd"));
    }
}
