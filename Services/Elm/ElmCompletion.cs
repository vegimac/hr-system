namespace HrSystem.Services.Elm;

/// <summary>
/// Completion-URL nach Transmitter-Richtlinien ELM 6.0, Anhang E (Foundation F08).
///
/// Die URL kommt aus dem XML schon entschlüsselt (XDocument löst «&amp;amp;» auf) —
/// sie wird hier NICHT nochmals dekodiert, sonst würde aus «%23» ein «#». Key und
/// Passwort werden URL-kodiert angehängt, mit «?» wenn die URL noch keine Parameter
/// hat, sonst mit «&amp;». Angezeigt werden Key und Passwort dagegen roh, damit man
/// sie per Copy-Paste in die Maske des Empfängers einfügen kann.
/// </summary>
public static class ElmCompletion
{
    public static string Url(string basis, string? key, string? password)
    {
        var url = (basis ?? "").Trim();
        var teile = new List<string>();
        if (!string.IsNullOrEmpty(key)) teile.Add("key=" + Uri.EscapeDataString(key));
        if (!string.IsNullOrEmpty(password)) teile.Add("password=" + Uri.EscapeDataString(password));
        if (teile.Count == 0 || url.Length == 0) return url;

        // Ein Anker gehört ans Ende — die Parameter davor einfügen.
        var anker = "";
        var raute = url.IndexOf('#');
        if (raute >= 0) { anker = url[raute..]; url = url[..raute]; }

        string trenner;
        if (!url.Contains('?')) trenner = "?";
        else if (url.EndsWith('?') || url.EndsWith('&')) trenner = "";
        else trenner = "&";
        return url + trenner + string.Join("&", teile) + anker;
    }
}
