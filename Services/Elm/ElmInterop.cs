using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace HrSystem.Services.Elm;

/// <summary>
/// Interoperabilität mit dem Empfänger (Swissdec Foundation-Test **F03**, Walter 24.09.2026).
///
/// Der Aufruf «CheckInteroperability» ist eine Rechenprobe über den ganzen Weg: Wir schicken
/// eine fest vorgegebene Zeichenkette mit Umlauten und eine fest vorgegebene Zahl, der
/// Empfänger schickt sie zurück und rechnet damit. Stimmt etwas nicht, liegt es am Encoding
/// oder am Zahlenformat — genau das soll die Prüfung ans Licht bringen.
///
/// Die Prüfpunkte im Wortlaut:
///   F03_01  Aufruf senden, Antwort auswerten und darstellen; UmlautString und FirstOperand
///           sind KONSTANT (der FirstOperand immer 9.99E11).
///   F03_02  Der SecondOperand ist wählbar — geprüft wird mit 0.01, 0.00 und −999'000'000'000.00.
///   F03_03  Der SecondOperand hat IMMER zwei Nachkommastellen.
///   F03_04  Verfälscht der Empfänger den UmlautString in der Antwort, erkennen wir das.
///   F03_05  Verfälscht der Empfänger den FirstOperand, erkennen wir das.
///
/// F03_04/F03_05 löst Swissdec in den RefApps aus; für uns heisst das: die Antwort wird
/// NACHGERECHNET und nicht geglaubt. Erst wenn alles stimmt, melden wir Interoperabilität.
/// </summary>
public static class ElmInterop
{
    /// <summary>Vorgegebene Zeichenkette aus dem XSD-Kommentar («use following UmlautString»).</summary>
    public const string UmlautString = "ÄËÖÜÁÉÓÚÀÈÒÙÂÊÔÛ";

    /// <summary>Was der Empfänger zurückschickt: dieselben Zeichen in Kleinbuchstaben.</summary>
    public const string UmlautErwartet = "äëöüáéóúàèòùâêôû";

    /// <summary>9.99E11 — laut XSD fest vorgegeben, NIE ein anderer Wert (F03_01).</summary>
    public const decimal FirstOperand = 999000000000.00m;

    /// <summary>Die drei von Swissdec verlangten Werte für den zweiten Operanden (F03_02).</summary>
    public static readonly decimal[] ZweiterOperandVorschlaege = { 0.01m, 0.00m, -999000000000.00m };

    /// <summary>
    /// Zahlformat von SalaryAmountType: <c>[\-]?[0-9]+\.[0-9]{2}</c> — Punkt als Trenner,
    /// immer genau zwei Nachkommastellen (F03_03).
    /// </summary>
    public static string Betrag(decimal wert) => wert.ToString("0.00", CultureInfo.InvariantCulture);

    private static readonly Regex BetragMuster = new(@"^-?[0-9]+\.[0-9]{2}$", RegexOptions.Compiled);

    /// <summary>Entspricht der Text exakt dem geforderten Zahlformat?</summary>
    public static bool IstBetragsFormat(string? text) => text != null && BetragMuster.IsMatch(text.Trim());

    /// <summary>
    /// Eingabe des Benutzers lesen. Komma wird als Dezimaltrenner akzeptiert und
    /// Tausendertrennzeichen entfernt; gerundet wird auf zwei Stellen, damit der
    /// gesendete Wert immer dem Format entspricht (F03_03). NULL = unlesbar.
    /// </summary>
    public static decimal? LiesBetrag(string? eingabe)
    {
        var t = (eingabe ?? "").Trim().Replace("'", "").Replace(" ", "").Replace(",", ".");
        if (t.Length == 0) return null;
        return decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out var w)
            ? Math.Round(w, 2, MidpointRounding.AwayFromZero)
            : null;
    }

    /// <summary>Ergebnis der Nachrechnung. <paramref name="Ok"/> nur, wenn ALLES stimmt.</summary>
    public record Befund(
        bool Ok,
        string Meldung,
        IReadOnlyList<string> Abweichungen,
        string? UmlautEcho,
        bool? UmlautBestaetigt,
        bool? FirstOperandBestaetigt,
        decimal? Addition,
        decimal? Subtraktion,
        decimal ErwarteteAddition,
        decimal ErwarteteSubtraktion);

    /// <summary>
    /// Antwort des Empfängers nachrechnen. Liefert NULL, wenn gar keine
    /// CheckInteroperability-Antwort vorliegt (z.B. bei einem Fault) — dann gibt es
    /// nichts zu prüfen und die Oberfläche zeigt den Fault.
    /// </summary>
    public static Befund? Pruefe(string? responseXml, decimal zweiterOperand)
    {
        if (string.IsNullOrWhiteSpace(responseXml)) return null;
        XElement? antwort;
        try
        {
            antwort = XDocument.Parse(responseXml).Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "CheckInteroperabilityResponse");
        }
        catch { return null; }
        if (antwort == null) return null;

        string? Text(string name) => antwort.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == name && !e.HasElements)?.Value.Trim();

        bool? Ja(string name) => Text(name) switch
        {
            "true" or "1" => true,
            "false" or "0" => false,
            _ => null,
        };

        var fehler = new List<string>();
        var echo = Text("UmlautString");
        var umlautOk = Ja("UmlautStringIsCorrect");
        var firstOk = Ja("FirstOperandIsCorrect");
        var additionText = Text("AdditionResult");
        var subtraktionText = Text("SubtractionResult");

        var sollAddition = FirstOperand + zweiterOperand;
        var sollSubtraktion = FirstOperand - zweiterOperand;

        // ── Was der Empfänger über UNSERE Sendung sagt ──────────────────────
        if (umlautOk == false)
            fehler.Add("Der Empfänger meldet, unsere Umlaute seien nicht korrekt angekommen.");
        if (firstOk == false)
            fehler.Add("Der Empfänger meldet, unsere erste Testzahl sei nicht korrekt angekommen.");

        // ── F03_04: Umlaute in der Antwort ──────────────────────────────────
        if (echo == null)
            fehler.Add("Die Antwort enthält keine Umlaut-Zeichenkette.");
        else if (!string.Equals(echo, UmlautErwartet, StringComparison.Ordinal))
            fehler.Add($"Die Umlaute der Antwort stimmen nicht: erwartet «{UmlautErwartet}», erhalten «{echo}».");

        // ── F03_05: Rechenergebnisse ────────────────────────────────────────
        decimal? Zahl(string? text, string bezeichnung)
        {
            if (text == null) { fehler.Add($"In der Antwort fehlt {bezeichnung}."); return null; }
            if (!IstBetragsFormat(text))
                fehler.Add($"{bezeichnung} hat nicht das geforderte Zahlformat (zwei Nachkommastellen): «{text}».");
            return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var w) ? w : null;
        }

        var addition = Zahl(additionText, "das Additionsergebnis");
        var subtraktion = Zahl(subtraktionText, "das Subtraktionsergebnis");

        if (addition.HasValue && addition.Value != sollAddition)
            fehler.Add($"Die Addition stimmt nicht: erwartet {Betrag(sollAddition)}, erhalten {Betrag(addition.Value)}.");
        if (subtraktion.HasValue && subtraktion.Value != sollSubtraktion)
            fehler.Add($"Die Subtraktion stimmt nicht: erwartet {Betrag(sollSubtraktion)}, erhalten {Betrag(subtraktion.Value)}.");

        var ok = fehler.Count == 0 && umlautOk == true && firstOk == true;
        var meldung = ok
            ? "Interoperabilität bestätigt: Sonderzeichen und Zahlen kommen unverändert an und werden richtig verrechnet."
            : fehler.Count > 0
                ? "Die Antwort des Empfängers ist nicht stimmig — Interoperabilität NICHT bestätigt."
                : "Der Empfänger hat die Prüfung nicht bestätigt — Interoperabilität NICHT bestätigt.";

        return new Befund(ok, meldung, fehler, echo, umlautOk, firstOk,
                          addition, subtraktion, sollAddition, sollSubtraktion);
    }
}
