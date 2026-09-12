namespace HrSystem.Services;

/// <summary>
/// Wann OneCrew eine Swissdec-Sonderkategorie vorschlagen oder nur warnen darf.
/// Nie still A/B/C/H überschreiben. Unklar → Amt abklären.
/// </summary>
public static class QstSonderkategorieErkennung
{
    public readonly record struct Ergebnis(
        string? VorschlagCode,
        bool BehoerdeAbklaeren,
        string Hinweis);

    public static Ergebnis Pruefe(
        string? aktuellerQstCode,
        bool wohnsitzAusland,
        bool hatBeteiligung1960,
        bool hatVrHonorar1500,
        bool hatNormalenLohn,
        bool kirchensteuer)
    {
        var aktuell = QstVordefinierteKategorie.Parse(aktuellerQstCode);

        if (hatBeteiligung1960 && hatVrHonorar1500)
            return new Ergebnis(null, true,
                "Im selben Monat Beteiligungsrechte (1960) und VR-Honorar (1500) — mit dem QST-Amt klären, welche Sonderkategorie gilt.");

        if (hatBeteiligung1960 && wohnsitzAusland)
        {
            var code = kirchensteuer ? "MEY" : "MEN";
            if (hatNormalenLohn)
                return new Ergebnis(code, true,
                    $"Wohnsitz Ausland und Lohnart 1960 plus normaler Lohn: Swissdec verlangt getrennte Behandlung. Sonderkategorie wäre {code} für die Beteiligung — mit dem QST-Amt abklären, nicht A/B/C/H auf alles legen.");
            return new Ergebnis(code, false,
                $"Wohnsitz Ausland und nur steuerbare Beteiligungsrechte → Sonderkategorie {code} (kein Tarif A/B/C/H, nicht ESTV-Tarif M).");
        }

        if (hatVrHonorar1500 && wohnsitzAusland)
        {
            var code = kirchensteuer ? "HEY" : "HEN";
            if (hatNormalenLohn)
                return new Ergebnis(code, true,
                    $"Wohnsitz Ausland und VR-Honorar plus normaler Lohn: Leistungen trennen. Sonderkategorie für das Honorar wäre {code} — mit dem QST-Amt abklären.");
            return new Ergebnis(code, false,
                $"Wohnsitz Ausland und nur VR-Honorar → Sonderkategorie {code} (kein Tarif A/B/C/H).");
        }

        if (aktuell != null && QstVordefinierteKategorie.IstNullAbzug(aktuell.Value.Art)
            && aktuell.Value.Art != QstVordefinierteKategorie.Art.Sfn)
            return new Ergebnis(aktuell.Value.Code, false,
                $"{aktuell.Value.Code} ist ein Korrektur-Code (nicht quellensteuerpflichtig), kein laufender Tarif und kein Ersatz für CH/C-Befreiung.");

        if (aktuell?.Art == QstVordefinierteKategorie.Art.Sfn)
            return new Ergebnis("SFN", false,
                "SFN gilt nur mit jährlicher FR-Ansässigkeitsbescheinigung und erfüllter Grenzgängerregel. Nachweis fehlt → ordentliche Tarife, ROT.");

        if (wohnsitzAusland && aktuell == null)
            return new Ergebnis(null, false, QstVordefinierteKategorie.HinweisWohnsitzAusland);

        return new Ergebnis(null, false, "");
    }
}
