using HrSystem.Models;

namespace HrSystem.Services;

/// <summary>
/// Interkantonale Vereinbarung der Familienausgleichskasse (Swissdec 8.2.4.2).
/// Liegt auf der bestehenden Filial-Zuordnung. Die Filiale bleibt im
/// Arbeitskanton. Zulagen an den Arbeitgeber folgen dem Tarif des
/// Hauptsitzkantons, die ELM-Meldung geht an dessen kantonale Kasse.
/// </summary>
public static class FakIkv
{
    /// <summary>
    /// Kanton, dessen Familienzulagen-Tarif gilt. Ohne Vereinbarung der
    /// Arbeitskanton. Der Arbeitskanton selbst wird nicht überschrieben.
    /// </summary>
    public static string? TarifKanton(string? arbeitsKanton, bool ikvAktiv, string? zielKanton)
    {
        if (ikvAktiv && !string.IsNullOrWhiteSpace(zielKanton))
            return zielKanton.Trim().ToUpperInvariant();
        return string.IsNullOrWhiteSpace(arbeitsKanton) ? null : arbeitsKanton.Trim().ToUpperInvariant();
    }

    /// <summary>Tarif-Kanton am Stichtag aus den Zuordnungen dieser Filiale.</summary>
    public static string? TarifKantonAm(
        string? arbeitsKanton, IEnumerable<CompanyProfileEmpfaenger> zuordnungen, DateOnly stichtag)
    {
        var z = zuordnungen
            .Where(x => x.IkvAktiv && x.GiltAm(stichtag)
                     && !string.IsNullOrWhiteSpace(x.IkvEmpfaenger?.KantonCode))
            .OrderByDescending(x => x.GueltigAb ?? DateOnly.MinValue)
            .FirstOrDefault();
        return TarifKanton(arbeitsKanton, z != null, z?.IkvEmpfaenger?.KantonCode);
    }

    /// <summary>Leer, wenn die Angaben stimmen. Sonst der Satz für die Maske.</summary>
    public static string? Pruefen(
        LohndatenEmpfaenger quelle, bool ikvAktiv, LohndatenEmpfaenger? ziel,
        string? arbeitsKanton, string? hauptsitzKanton)
    {
        if (!ikvAktiv) return null;
        if (!IstKantonalFak(quelle))
            return "Eine interkantonale Vereinbarung ist nur bei einer kantonalen Familienausgleichskasse möglich.";
        if (ziel == null)
            return "Bitte die Familienausgleichskasse des Hauptsitzes wählen.";
        if (!IstKantonalFak(ziel))
            return "Die Zielkasse muss eine kantonale Familienausgleichskasse sein.";
        if (ziel.Id != 0 && quelle.Id != 0 && ziel.Id == quelle.Id)
            return "Die Zielkasse muss eine andere Kasse sein.";
        var hs = Norm(hauptsitzKanton);
        var arb = Norm(arbeitsKanton);
        var zielKt = Norm(ziel.KantonCode);
        if (hs.Length != 2)
            return "Der Hauptsitz hat keinen Kanton. Diesen zuerst in den Hauptsitz-Stammdaten setzen.";
        if (arb.Length != 2)
            return "Die Filiale hat keinen Standort-Kanton.";
        if (arb == hs)
            return "Arbeitskanton und Hauptsitzkanton sind gleich. Eine interkantonale Vereinbarung braucht zwei Kantone.";
        if (zielKt != hs)
            return $"Die Zielkasse muss zum Hauptsitzkanton {hs} gehören.";
        var nummer = AusgleichskasseNummer.Fehler(ziel.Kassennummer, AusgleichskasseNummer.BegriffFak);
        return nummer;
    }

    public static bool IstKantonalFak(LohndatenEmpfaenger? e)
    {
        if (e == null || !string.Equals(e.Art, "FAK", StringComparison.OrdinalIgnoreCase)) return false;
        var art = e.KassenTyp;
        if (string.IsNullOrWhiteSpace(art))
            art = KassenArt.AusNummer(e.Art, e.Kassennummer);
        return string.Equals(art, KassenArt.Kantonal, StringComparison.OrdinalIgnoreCase);
    }

    static string Norm(string? kanton) =>
        string.IsNullOrWhiteSpace(kanton) ? "" : kanton.Trim().ToUpperInvariant();
}
