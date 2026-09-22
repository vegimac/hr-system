using HrSystem.Models;

namespace HrSystem.Services;

/// <summary>
/// Beruflicher Werdegang für Arbeitszeugnis / Zwischenzeugnis / Arbeitsbestätigung
/// (Walter-Vorgabe 22.09.2026): die Vertragsabschnitte des MA als Zeilen
/// «04.09.2024 – 30.09.2025 · Crewmitarbeiterin im Stundenlohn».
///
/// Drei Regeln, die den Text lesbar halten:
///  1. Aufeinanderfolgende Abschnitte mit GLEICHER Funktion und gleichem Modell
///     werden zusammengefasst (Lohnerhöhung/Pensumwechsel erzeugt keinen Eintrag).
///  2. Das Vertragsmodell steht nur dann in der Zeile, wenn es sich im Werdegang
///     überhaupt ändert — sonst liest sich jede Zeile wie ein Formular.
///  3. Der laufende Abschnitt (kein Ende oder Ende in der Zukunft) heisst «seit …».
///
/// «Shift Leader» heisst im Zeugnis **Schichtführer/in** (Walter 22.09.2026);
/// die ersten 6 Monate sind «Schichtführer/in in Ausbildung» (L-GAV-Stufe
/// SHIFT_LEADER_1_6), danach ohne Zusatz.
/// </summary>
public static class ZeugnisWerdegang
{
    /// <summary>Ein Abschnitt für die Auswahl im UI und fürs PDF.</summary>
    public record Abschnitt(
        DateOnly Von,
        DateOnly? Bis,
        string Funktion,
        string Modell,
        string ModellText,
        string Text);

    /// <summary>Bekannte Funktions-Codes (job_group.code).</summary>
    private static readonly HashSet<string> Codes = new(StringComparer.OrdinalIgnoreCase)
    {
        "CREW", "HOST_CT", "SWING", "SHIFT_LEADER_1_6", "SHIFT_LEADER_7_PLUS",
        "ASST_2", "ASST_1", "REST_MANAGER",
    };

    /// <summary>
    /// Funktionsbezeichnung fürs Zeugnis (weiblich/männlich).
    /// **Quellen-Reihenfolge (Walter 22.09.2026):** `employment.job_title` führt im
    /// Altbestand den Funktions-Code PRO Vertragsabschnitt und ist damit historisch
    /// korrekt; `job_group_id` wird vom easy@work-Sync oft auf die HEUTIGE Funktion
    /// gesetzt (easy liefert nur eine). Trägt job_title einen bekannten Code, gilt der;
    /// sonst die Funktionsgruppe; sonst der Freitext («Shift Coordinator»).
    /// </summary>
    public static string FunktionText(string? jobGroupCode, string? jobTitle, bool female)
    {
        var titel = (jobTitle ?? "").Trim();
        var code = Codes.Contains(titel) ? titel.ToUpperInvariant() : (jobGroupCode ?? "").Trim().ToUpperInvariant();
        string w(string m, string f) => female ? f : m;
        return code switch
        {
            "CREW"                => w("Crewmitarbeiter", "Crewmitarbeiterin"),
            "HOST_CT"             => w("Crew-Trainer", "Crew-Trainerin"),
            "SWING"               => w("Swing Manager", "Swing Managerin"),
            "SHIFT_LEADER_1_6"    => w("Schichtführer in Ausbildung", "Schichtführerin in Ausbildung"),
            "SHIFT_LEADER_7_PLUS" => w("Schichtführer", "Schichtführerin"),
            "ASST_2"              => w("Assistant Manager", "Assistant Managerin"),
            "ASST_1"              => w("Erster Assistant Manager", "Erste Assistant Managerin"),
            "REST_MANAGER"        => w("Geschäftsführer", "Geschäftsführerin"),
            _ => string.IsNullOrWhiteSpace(jobTitle)
                    ? w("Mitarbeiter", "Mitarbeiterin")
                    : jobTitle!.Trim(),
        };
    }

    /// <summary>Modell als Klartext — FIX/FIX-M bewusst gleich («im Monatslohn»).</summary>
    public static string ModellText(string? model) => (model ?? "").Trim().ToUpperInvariant() switch
    {
        "FLEX" or "UTP" => "im Stundenlohn",
        "MTP"           => "mit garantierten Stunden",
        "FIX" or "FIX-M" => "im Monatslohn",
        _               => "",
    };

    /// <summary>
    /// Baut die Werdegang-Zeilen. <paramref name="stichtag"/> entscheidet, was
    /// «seit …» ist (Zeugnis-Datum); <paramref name="bis"/> begrenzt den letzten
    /// Abschnitt beim Schlusszeugnis (fiktives Austrittsdatum aus der Maske).
    /// </summary>
    public static List<Abschnitt> Baue(
        IEnumerable<Employment> employments, bool female, DateOnly stichtag, DateOnly? bis = null)
    {
        var roh = employments
            .Where(e => e.ContractStartDate.Year > 1)
            .OrderBy(e => e.ContractStartDate)
            .Select(e => new
            {
                Von = DateOnly.FromDateTime(e.ContractStartDate),
                Bis = e.ContractEndDate.HasValue ? DateOnly.FromDateTime(e.ContractEndDate.Value) : (DateOnly?)null,
                Funktion = FunktionText(e.JobGroupCode, e.JobTitle, female),
                Modell = (e.EmploymentModel ?? "").Trim().ToUpperInvariant(),
            })
            .ToList();
        if (roh.Count == 0) return new List<Abschnitt>();

        // 1) Gleiche Funktion + gleiches Modell direkt hintereinander = ein Abschnitt.
        var zusammen = new List<(DateOnly Von, DateOnly? Bis, string Funktion, string Modell)>();
        foreach (var r in roh)
        {
            if (zusammen.Count > 0)
            {
                var letzt = zusammen[^1];
                if (letzt.Funktion == r.Funktion && letzt.Modell == r.Modell)
                {
                    // Ende nach hinten schieben (offenes Ende gewinnt).
                    var neuesEnde = letzt.Bis.HasValue && r.Bis.HasValue
                        ? (r.Bis > letzt.Bis ? r.Bis : letzt.Bis)
                        : null;
                    zusammen[^1] = (letzt.Von, neuesEnde, letzt.Funktion, letzt.Modell);
                    continue;
                }
            }
            zusammen.Add((r.Von, r.Bis, r.Funktion, r.Modell));
        }

        // Letzten Abschnitt auf das Zeugnis-Ende begrenzen (Schlusszeugnis).
        if (bis.HasValue)
        {
            var letzt = zusammen[^1];
            if (!letzt.Bis.HasValue || letzt.Bis > bis.Value)
                zusammen[^1] = (letzt.Von, bis.Value, letzt.Funktion, letzt.Modell);
        }

        // 2) Modell nur nennen, wenn es sich im Werdegang ändert.
        bool modellNennen = zusammen.Select(x => x.Modell).Distinct().Count() > 1;

        var result = new List<Abschnitt>();
        foreach (var z in zusammen)
        {
            var mText = modellNennen ? ModellText(z.Modell) : "";
            var funktionVoll = string.IsNullOrEmpty(mText) ? z.Funktion : $"{z.Funktion} {mText}";
            // 3) Laufend = «seit …».
            bool laufend = !z.Bis.HasValue || z.Bis.Value >= stichtag;
            var zeit = laufend
                ? $"seit {z.Von:dd.MM.yyyy}"
                : $"{z.Von:dd.MM.yyyy} – {z.Bis:dd.MM.yyyy}";
            result.Add(new Abschnitt(z.Von, z.Bis, z.Funktion, z.Modell, mText, $"{zeit} · {funktionVoll}"));
        }
        return result;
    }
}
