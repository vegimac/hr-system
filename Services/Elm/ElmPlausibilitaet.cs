using System.Globalization;
using System.Xml.Linq;
using static HrSystem.Services.Elm.ElmUebermittlungXml;

namespace HrSystem.Services.Elm;

/// <summary>
/// Plausibilitätsprüfung im Transmitter VOR dem Senden (Foundation F06_01, Baustein
/// AB-12 Punkt 2: «Das Sendersystem kann die Meldung vor dem Versenden auf ihre
/// Plausibilität prüfen»). Geprüft wird die fertige Meldung — also genau das, was
/// hinausginge, nicht die Datenbank. Ein Fehler hier heisst: nicht senden.
/// Der Test-Distributor der RefApps prüft diese Regeln nicht (30.09.2026: Alter 126
/// und falsche AHV-Prüfziffer wurden ohne Hinweis angenommen).
/// </summary>
public static class ElmPlausibilitaet
{
    public const string CodeAlter = "P-ALTER";
    public const string CodeAhv = "P-AHV";
    public const string CodeDatum = "P-DATUM";

    /// <summary>Beispielregel der Spezifikation (Kap. 11.17.1.2): «Alter muss kleiner als 100 Jahre sein».</summary>
    public const int HoechstAlter = 100;

    /// <param name="stichtag">Letzter Tag der Meldeperiode (Monatsende bzw. 31.12.).</param>
    public static List<Hinweis> Pruefe(XElement meldung, DateTime stichtag)
    {
        var fehler = new List<Hinweis>();
        foreach (var person in meldung.Descendants().Where(e => e.Name.LocalName == "Person"))
        {
            var part = Kind(person, "Particulars");
            if (part == null) continue;
            var wer = Wer(part);

            var geb = Datum(Kind(part, "DateOfBirth")?.Value);
            if (geb != null)
            {
                if (geb > stichtag)
                    fehler.Add(Fehler(CodeDatum, $"{wer}: Geburtsdatum {geb:dd.MM.yyyy} liegt nach dem Ende der Meldeperiode."));
                else
                {
                    var alter = Alter(geb.Value, stichtag);
                    if (alter >= HoechstAlter)
                        fehler.Add(Fehler(CodeAlter, $"{wer}: Alter {alter} Jahre (Geburtsdatum {geb:dd.MM.yyyy}) — "
                            + $"Regel «Alter muss kleiner als {HoechstAlter} Jahre sein»."));
                }
            }

            var ahv = Kind(Kind(part, "Social-InsuranceIdentification"), "SV-AS-Number")?.Value;
            if (ahv != null && !AhvPruefzifferStimmt(ahv))
                fehler.Add(Fehler(CodeAhv, $"{wer}: AHV-Nummer {ahv} hat eine falsche Prüfziffer."));

            foreach (var work in person.Elements().Where(e => e.Name.LocalName == "Work"))
            {
                var eintritt = Datum(Kind(work, "EntryDate")?.Value);
                var austritt = Datum(Kind(work, "WithdrawalDate")?.Value);
                if (eintritt != null && austritt != null && austritt < eintritt)
                    fehler.Add(Fehler(CodeDatum, $"{wer}: Austritt {austritt:dd.MM.yyyy} liegt vor dem Eintritt {eintritt:dd.MM.yyyy}."));
                if (eintritt != null && geb != null && eintritt < geb)
                    fehler.Add(Fehler(CodeDatum, $"{wer}: Eintritt {eintritt:dd.MM.yyyy} liegt vor dem Geburtsdatum {geb:dd.MM.yyyy}."));
            }
        }
        return fehler;
    }

    /// <summary>
    /// AHV-Nummer (756.xxxx.xxxx.xx): Prüfziffer nach EAN-13 — Gewichte 1 und 3
    /// abwechselnd auf die ersten zwölf Ziffern, Prüfziffer = (10 − Summe mod 10) mod 10.
    /// </summary>
    public static bool AhvPruefzifferStimmt(string ahv)
    {
        var d = new string(ahv.Where(char.IsDigit).ToArray());
        if (d.Length != 13) return false;
        var summe = 0;
        for (int i = 0; i < 12; i++) summe += (d[i] - '0') * (i % 2 == 0 ? 1 : 3);
        return (10 - summe % 10) % 10 == d[12] - '0';
    }

    private static int Alter(DateTime geb, DateTime stichtag)
    {
        var a = stichtag.Year - geb.Year;
        if (stichtag.Month < geb.Month || (stichtag.Month == geb.Month && stichtag.Day < geb.Day)) a--;
        return a;
    }

    private static Hinweis Fehler(string code, string text) => new("Error", "Plausibility", code, text, null);

    private static XElement? Kind(XElement? e, string name)
        => e?.Elements().FirstOrDefault(k => k.Name.LocalName == name);

    private static DateTime? Datum(string? s)
        => DateTime.TryParseExact(s?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;

    private static string Wer(XElement part)
    {
        var name = $"{Kind(part, "Firstname")?.Value} {Kind(part, "Lastname")?.Value}".Trim();
        var nr = Kind(part, "EmployeeNumber")?.Value;
        return string.IsNullOrWhiteSpace(nr) ? name : $"{name} ({nr})";
    }
}
