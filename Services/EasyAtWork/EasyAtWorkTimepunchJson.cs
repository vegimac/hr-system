using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HrSystem.Services.EasyAtWork;

/// <summary>
/// Einzelabruf eines Stempels lesen. Kommentare und Changelog sind nicht
/// dokumentiert (Array oder { data: [...] }, `user` als Text oder Objekt) —
/// sie werden darum von Hand gelesen, damit ein unerwarteter Typ dort nie den
/// ganzen Stempel unlesbar macht.
/// </summary>
public static class EasyAtWorkTimepunchJson
{
    private static readonly string[] TextFelder = { "text", "comment", "body", "message", "content", "description" };
    private static readonly string[] NameFelder = { "created_by_name", "user_name", "author_name", "causer_name" };

    public static EawTimepunch? Lesen(string body, JsonSerializerOptions opts)
    {
        var node = JsonNode.Parse(body);
        if (node is JsonObject wurzel && wurzel["data"] is JsonObject daten) node = daten;
        if (node is not JsonObject tpObj) return null;

        var kommentare = tpObj["comments"];
        var changelog  = tpObj["changelog"];
        tpObj.Remove("comments");
        tpObj.Remove("changelog");

        var tp = tpObj.Deserialize<EawTimepunch>(opts);
        if (tp == null) return null;
        tp.Comments  = Eintraege(kommentare).Select(KommentarAus).Where(k => k.AnyText != null).ToList();
        tp.Changelog = Eintraege(changelog).Select(ChangelogAus).Where(c => c.AnyText != null).ToList();
        return tp;
    }

    private static IEnumerable<JsonObject> Eintraege(JsonNode? n)
    {
        if (n is JsonObject o && o["data"] is JsonArray d) n = d;
        return n is JsonArray a ? a.OfType<JsonObject>() : Enumerable.Empty<JsonObject>();
    }

    private static EawTimepunchComment KommentarAus(JsonObject o) => new()
    {
        Text          = Text(o),
        CreatedAt     = Zeit(o["created_at"]),
        CreatedByName = Name(o),
    };

    private static EawTimepunchChangelogEntry ChangelogAus(JsonObject o) => new()
    {
        Text          = Text(o),
        CreatedAt     = Zeit(o["created_at"]),
        CreatedByName = Name(o),
    };

    private static string? Text(JsonObject o)
        => TextFelder.Select(f => Wert(o[f])).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    private static string? Name(JsonObject o)
    {
        foreach (var f in NameFelder)
        {
            var v = Wert(o[f]);
            if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
        }
        foreach (var f in new[] { "user", "created_by", "author" })
        {
            var n = o[f];
            if (n is JsonObject u)
            {
                var name = Wert(u["name"]) ?? Wert(u["full_name"])
                    ?? string.Join(" ", new[] { Wert(u["firstname"]) ?? Wert(u["first_name"]), Wert(u["lastname"]) ?? Wert(u["last_name"]) }
                        .Where(s => !string.IsNullOrWhiteSpace(s)));
                if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
            }
            else
            {
                var s = Wert(n);
                if (!string.IsNullOrWhiteSpace(s) && !int.TryParse(s, out _)) return s.Trim();
            }
        }
        return null;
    }

    private static string? Wert(JsonNode? n)
        => n is JsonValue v && v.TryGetValue<JsonElement>(out var e)
            ? e.ValueKind switch
            {
                JsonValueKind.String => e.GetString(),
                JsonValueKind.Number => e.GetRawText(),
                _ => null,
            }
            : null;

    private static DateTime? Zeit(JsonNode? n)
    {
        var s = Wert(n);
        if (string.IsNullOrWhiteSpace(s)) return null;
        return DateTime.TryParse(s, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt)
            ? DateTime.SpecifyKind(dt, DateTimeKind.Utc)
            : null;
    }
}
