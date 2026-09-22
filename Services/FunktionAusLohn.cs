using HrSystem.Models;

namespace HrSystem.Services;

/// <summary>
/// Funktion eines Vertragsabschnitts aus dem LOHN rekonstruieren (Walter-Vorgabe 22.09.2026).
///
/// Warum das nötig ist: easy@work liefert die Funktion OHNE Historie (`/positions` ist ein
/// reiner Pivot ohne from/to, `cf_src_job_code` ebenso). Bis zum Riegel vom 22.09.2026 hat
/// der Sync den heutigen Stand auf ALLE Vertragsabschnitte geschrieben — die Vergangenheit
/// ist dort überschrieben. Die LÖHNE sind dagegen sauber versioniert (easy `pay_rates` mit
/// from/to, bei uns pro Vertragsabschnitt), und der L-GAV-Mindestlohn ist je Funktion
/// unterschiedlich. Daraus lässt sich die Funktion zurückrechnen.
///
/// Ergebnis ist immer ein VORSCHLAG mit Sicherheitsgrad — nie eine stille Korrektur:
/// ein Arbeitszeugnis mit erfundener Funktion wäre ein falsches Rechtsdokument.
/// </summary>
public static class FunktionAusLohn
{
    public enum Sicherheit
    {
        /// <summary>Lohn trifft genau einen Mindestlohn-Satz.</summary>
        Exakt,
        /// <summary>Lohn liegt über dem Minimum — höchste Funktion darunter gewählt.</summary>
        Ungefaehr,
        /// <summary>Mehrere Funktionen teilen diesen Satz / keine Sätze vorhanden.</summary>
        Unklar,
    }

    public record Vorschlag(string? JobGroupCode, Sicherheit Sicherheit, string Begruendung);

    /// <summary>Ein Mindestlohn-Satz, auf das Nötige reduziert (DB-frei testbar).</summary>
    public record Satz(string JobGroupCode, string EmploymentModelCode, string SalaryType,
                       decimal Amount, DateOnly ValidFrom, DateOnly? ValidTo, string? EducationLevelCode);

    /// <summary>
    /// Rekonstruiert die Funktion eines Abschnitts. <paramref name="saetze"/> = alle
    /// Mindestlohn-Regeln (werden hier nach Datum/Modell/Lohnart/Stufe gefiltert).
    /// </summary>
    public static Vorschlag Ermittle(
        DateOnly vertragsbeginn,
        string? employmentModel,
        string? salaryType,
        decimal? stundenlohn,
        decimal? monatslohn100,
        string? educationLevelCode,
        IEnumerable<Satz> saetze)
    {
        var model = (employmentModel ?? "").Trim().ToUpperInvariant();
        bool monatlich = string.Equals(salaryType, "monthly", StringComparison.OrdinalIgnoreCase)
                         || model is "FIX" or "FIX-M";
        decimal? lohn = monatlich ? monatslohn100 : stundenlohn;
        if (lohn is not > 0)
            return new Vorschlag(null, Sicherheit.Unklar, "Kein Lohn am Vertrag erfasst.");

        var art = monatlich ? "monthly" : "hourly";
        var passend = saetze
            .Where(s => string.Equals(s.SalaryType, art, StringComparison.OrdinalIgnoreCase))
            .Where(s => s.ValidFrom <= vertragsbeginn && (s.ValidTo == null || s.ValidTo >= vertragsbeginn))
            // Modell: exakte Übereinstimmung, sonst alle (FIX/FIX-M teilen die Monatssätze).
            .Where(s => string.IsNullOrWhiteSpace(s.EmploymentModelCode)
                     || string.Equals(s.EmploymentModelCode, model, StringComparison.OrdinalIgnoreCase))
            .ToList();
        // Ausbildungsstufe eingrenzen, wenn am Vertrag erfasst — sonst über alle Stufen
        // suchen (dann ist die Mehrdeutigkeit grösser, was der Sicherheitsgrad abbildet).
        if (!string.IsNullOrWhiteSpace(educationLevelCode))
        {
            var mitStufe = passend
                .Where(s => string.Equals(s.EducationLevelCode, educationLevelCode, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (mitStufe.Count > 0) passend = mitStufe;
        }
        if (passend.Count == 0)
            return new Vorschlag(null, Sicherheit.Unklar,
                $"Keine Mindestlohn-Sätze für {art} / {model} per {vertragsbeginn:dd.MM.yyyy} gefunden.");

        // 1) Exakter Treffer (Rappen-Toleranz): genau EINE Funktion mit diesem Betrag.
        var exakt = passend
            .Where(s => Math.Abs(s.Amount - lohn.Value) <= 0.05m)
            .Select(s => s.JobGroupCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (exakt.Count == 1)
            return new Vorschlag(exakt[0], Sicherheit.Exakt,
                $"Lohn {lohn:0.00} entspricht dem Mindestlohn von {exakt[0]}.");
        if (exakt.Count > 1)
            return new Vorschlag(Hoechste(exakt, passend), Sicherheit.Unklar,
                $"Lohn {lohn:0.00} passt auf mehrere Funktionen ({string.Join(", ", exakt)}).");

        // 2) Kein exakter Treffer: höchste Funktion, deren Minimum ≤ Lohn.
        var darunter = passend.Where(s => s.Amount <= lohn.Value).ToList();
        if (darunter.Count == 0)
            return new Vorschlag(null, Sicherheit.Unklar,
                $"Lohn {lohn:0.00} liegt unter allen Mindestlöhnen — bitte prüfen.");
        var maxBetrag = darunter.Max(s => s.Amount);
        var kandidaten = darunter.Where(s => s.Amount == maxBetrag)
            .Select(s => s.JobGroupCode).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new Vorschlag(Hoechste(kandidaten, passend),
            kandidaten.Count == 1 ? Sicherheit.Ungefaehr : Sicherheit.Unklar,
            $"Lohn {lohn:0.00} liegt über dem Mindestlohn von {string.Join("/", kandidaten)} ({maxBetrag:0.00}).");
    }

    /// <summary>Bei mehreren Kandidaten die höherwertige Funktion (Rangfolge L-GAV).</summary>
    private static string Hoechste(IEnumerable<string> codes, IEnumerable<Satz> _)
    {
        var rang = new[] { "CREW", "HOST_CT", "SWING", "SHIFT_LEADER_1_6", "SHIFT_LEADER_7_PLUS",
                           "ASST_2", "ASST_1", "REST_MANAGER" };
        return codes.OrderByDescending(c => Array.FindIndex(rang, r => string.Equals(r, c, StringComparison.OrdinalIgnoreCase)))
                    .First();
    }

    /// <summary>
    /// Schichtführer-Stufe nach Zeit statt nach Lohn (L-GAV): die ersten 6 Monate ab dem
    /// ERSTEN Schichtführer-Vertrag sind «1–6 Mt.», danach «7+». Alte Daten stehen oft auf
    /// SHIFT_LEADER_1_6 stehen, obwohl die Person längst darüber ist.
    /// </summary>
    public static string SchichtfuehrerStufe(DateOnly ersterSchichtfuehrerBeginn, DateOnly abschnittBeginn)
        => abschnittBeginn < ersterSchichtfuehrerBeginn.AddMonths(6)
            ? "SHIFT_LEADER_1_6"
            : "SHIFT_LEADER_7_PLUS";
}
