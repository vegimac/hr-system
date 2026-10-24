using HrSystem.Models;

namespace HrSystem.Services;

/// <summary>
/// Verwendete Lohnart: die Steuerung bleibt, Löschen geht nicht
/// (Swissdec Lohnartenstamm, Richtlinie 5.1). Verwendet heisst: der Code
/// steht auf einem gespeicherten Lohnzettel. Bezeichnung, Mirus-Nummer
/// und Sortierung bleiben frei — die ändern keinen vergangenen Lohn.
/// </summary>
public static class LohnartSperre
{
    public const string Fehler = "LOHNART_GESPERRT";

    public static string MeldungLoeschen(string? code, string? bezeichnung)
        => $"Die Lohnart «{Text(code, bezeichnung)}» wurde bereits in einem Lohnlauf verwendet. Löschen ist nicht möglich.";

    public static string MeldungAendern(string? code, string? bezeichnung)
        => $"Die Lohnart «{Text(code, bezeichnung)}» wurde bereits in einem Lohnlauf verwendet. Die Steuerung bleibt unverändert.";

    /// <summary>
    /// true, wenn Versicherung, Vorzeichen, Quellensteuer, Lohnausweis,
    /// Lohnbasen, Nummer oder Aktiv-Flag anders würden.
    /// </summary>
    public static bool SteuerungGeaendert(Lohnposition ist, Lohnposition neu)
        => !Gleich(ist.Code, neu.Code)
        || !Gleich(ist.Typ, neu.Typ)
        || ist.AhvAlvPflichtig != neu.AhvAlvPflichtig
        || ist.NbuvPflichtig != neu.NbuvPflichtig
        || ist.UvgPflichtig != neu.UvgPflichtig
        || ist.UvgzPflichtig != neu.UvgzPflichtig
        || ist.KtgPflichtig != neu.KtgPflichtig
        || ist.BvgPflichtig != neu.BvgPflichtig
        || ist.QstPflichtig != neu.QstPflichtig
        || ist.QstPeriodisch != neu.QstPeriodisch
        || !Gleich(ist.QstCharakter, neu.QstCharakter)
        || !Gleich(ist.BvgBetrachtung, neu.BvgBetrachtung)
        || ist.BvgFaktor != neu.BvgFaktor
        || !Gleich(ist.StatistikMonat, neu.StatistikMonat)
        || !Gleich(ist.StatistikJahr, neu.StatistikJahr)
        || ist.DreijehnterMlPflichtig != neu.DreijehnterMlPflichtig
        || ist.ZaehltAlsBasisFeiertag != neu.ZaehltAlsBasisFeiertag
        || ist.ZaehltAlsBasisFerien != neu.ZaehltAlsBasisFerien
        || ist.ZaehltAlsBasis13ml != neu.ZaehltAlsBasis13ml
        || !Gleich(ist.LohnausweisCode, neu.LohnausweisCode)
        || !Gleich(ist.SwissdecLohnart, neu.SwissdecLohnart)
        || ist.IsActive != neu.IsActive;

    static string Text(string? code, string? bezeichnung)
    {
        var c = (code ?? "").Trim();
        var b = (bezeichnung ?? "").Trim();
        if (c.Length == 0) return b.Length == 0 ? "ohne Nummer" : b;
        return b.Length == 0 ? c : c + " " + b;
    }

    static bool Gleich(string? a, string? b)
        => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.Ordinal);
}
