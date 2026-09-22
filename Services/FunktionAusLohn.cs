namespace HrSystem.Services;

/// <summary>
/// Funktion eines Vertragsabschnitts aus dem LOHN bestimmen (Walter-Vorgabe 22.09.2026).
///
/// Warum das nötig ist: easy@work liefert die Funktion OHNE Historie (`/positions` ist ein
/// reiner Pivot ohne from/to, `cf_src_job_code` ebenso). Bis zum Sync-Riegel vom 22.09.2026
/// hat der Sync den heutigen Stand auf ALLE Vertragsabschnitte geschrieben — die Vergangenheit
/// ist dort überschrieben. Die LÖHNE sind dagegen sauber versioniert (easy `pay_rates` mit
/// from/to, bei uns pro Vertragsabschnitt).
///
/// **Feste Schwellen statt Raster-Treffer (Walter-Entscheid 22.09.2026):** die L-GAV-Sätze
/// ändern jährlich (Crew 20.14 / 20.36 / 20.40), die Zuordnung «welcher Lohn = welche
/// Funktion» soll über alle Jahre gleich bleiben. Deshalb entscheiden die Konstanten unten
/// und NICHT der Jahres-Satz im Mindestlohn-Raster. Ändern sich die Sätze wesentlich, werden
/// genau diese Zahlen angepasst — an dieser einen Stelle.
///
/// Ergebnis ist ein VORSCHLAG mit Sicherheitsgrad — nie eine stille Korrektur:
/// ein Arbeitszeugnis mit erfundener Funktion wäre ein falsches Rechtsdokument.
/// </summary>
public static class FunktionAusLohn
{
    // ── Schwellen (Walter 22.09.2026) ────────────────────────────────────────
    /// <summary>Stundenlohn bis und mit diesem Betrag = Crew.</summary>
    public const decimal StundenCrewBis = 20.40m;
    /// <summary>Stundenlohn bis und mit diesem Betrag = Crew-Trainer/in (Host CT).</summary>
    public const decimal StundenHostBis = 21.66m;
    /// <summary>Monatslohn (100 %) unter diesem Betrag = Schichtführer/in in Ausbildung.</summary>
    public const decimal MonatSl16Unter = 4500m;
    /// <summary>Monatslohn (100 %) unter diesem Betrag = Schichtführer/in; darüber Geschäftsführer/in.</summary>
    public const decimal MonatSl7Unter = 5000m;

    public enum Sicherheit
    {
        /// <summary>Lohn liegt eindeutig in einem Schwellenband.</summary>
        Exakt,
        /// <summary>Band getroffen, aber ein Umstand mahnt zur Prüfung.</summary>
        Ungefaehr,
        /// <summary>Kein Lohn erfasst / Ausbildungsstufe macht den Lohn blind.</summary>
        Unklar,
    }

    public record Vorschlag(string? JobGroupCode, Sicherheit Sicherheit, string Begruendung);

    /// <summary>
    /// Ausbildungsstufen, in denen der Lohn nichts über die Funktion aussagt (Walter-Entscheid
    /// 22.09.2026, Punkt 4): ab Stufe II verdient auch eine Crew 22.36 und mehr — mit der festen
    /// Grenze würde daraus fälschlich ein Swing Manager. Nur Ia/Ib (oder keine Stufe erfasst)
    /// lassen die Lohn-Regel greifen.
    /// </summary>
    private static bool StufeErlaubtLohnregel(string? code)
    {
        var c = (code ?? "").Trim();
        return c.Length == 0
            || c.Equals("Ia", StringComparison.OrdinalIgnoreCase)
            || c.Equals("Ib", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Funktion aus dem Lohn. <paramref name="monatslohn100"/> muss auf 100 % hochgerechnet
    /// sein (ein 60 %-Schichtführer verdient 2'580 — die Schwellen gelten für das Vollpensum).
    /// </summary>
    public static Vorschlag Ermittle(
        DateOnly vertragsbeginn,
        string? employmentModel,
        string? salaryType,
        decimal? stundenlohn,
        decimal? monatslohn100,
        string? educationLevelCode)
    {
        var model = (employmentModel ?? "").Trim().ToUpperInvariant();
        bool monatlich = string.Equals(salaryType, "monthly", StringComparison.OrdinalIgnoreCase)
                         || model is "FIX" or "FIX-M";
        decimal? lohn = monatlich ? monatslohn100 : stundenlohn;
        if (lohn is not > 0)
            return new Vorschlag(null, Sicherheit.Unklar, "Kein Lohn am Vertrag erfasst.");

        if (!StufeErlaubtLohnregel(educationLevelCode))
            return new Vorschlag(null, Sicherheit.Unklar,
                $"Ausbildungsstufe {educationLevelCode} — der Lohn sagt hier nichts über die Funktion aus.");

        if (monatlich)
        {
            // Monatslohn = Kader-Bereich. Unter 4'300 (100 %) gibt es bei Schaub keine
            // Monatslöhner (Walter 22.09.2026) — käme trotzdem einer, ist das ein
            // Datenfehler und KEIN Schichtführer-Vorschlag.
            if (lohn < 4300m)
                return new Vorschlag(null, Sicherheit.Unklar,
                    $"Monatslohn {lohn:0.00} (100 %) liegt unter dem Schichtführer-Bereich — bitte prüfen.");

            var code = lohn < MonatSl16Unter ? "SHIFT_LEADER_1_6"
                     : lohn < MonatSl7Unter  ? "SHIFT_LEADER_7_PLUS"
                     :                         "REST_MANAGER";
            var grenze = lohn < MonatSl16Unter ? $"< {MonatSl16Unter:0}"
                       : lohn < MonatSl7Unter  ? $"{MonatSl16Unter:0}–{MonatSl7Unter - 1:0}"
                       :                         $"≥ {MonatSl7Unter:0}";
            return new Vorschlag(code, Sicherheit.Exakt,
                $"Monatslohn {lohn:0.00} (100 %) {grenze} → {code}.");
        }

        var hCode = lohn <= StundenCrewBis ? "CREW"
                  : lohn <= StundenHostBis ? "HOST_CT"
                  :                          "SWING";
        var hGrenze = lohn <= StundenCrewBis ? $"≤ {StundenCrewBis:0.00}"
                    : lohn <= StundenHostBis ? $"{StundenCrewBis:0.00}–{StundenHostBis:0.00}"
                    :                          $"> {StundenHostBis:0.00}";
        return new Vorschlag(hCode, Sicherheit.Exakt, $"Stundenlohn {lohn:0.00} {hGrenze} → {hCode}.");
    }

    /// <summary>
    /// Schichtführer-Stufe nach Zeit statt nach Lohn (L-GAV): die ersten 6 Monate ab dem
    /// ERSTEN Schichtführer-Vertrag sind «1–6 Mt.», danach «7+». Alte Daten stehen oft auf
    /// SHIFT_LEADER_1_6, obwohl die Person längst darüber ist.
    /// </summary>
    public static string SchichtfuehrerStufe(DateOnly ersterSchichtfuehrerBeginn, DateOnly abschnittBeginn)
        => abschnittBeginn < ersterSchichtfuehrerBeginn.AddMonths(6)
            ? "SHIFT_LEADER_1_6"
            : "SHIFT_LEADER_7_PLUS";
}
