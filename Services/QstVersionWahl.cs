using HrSystem.Models;

namespace HrSystem.Services;

/// <summary>
/// QST-Version am Stichtag: «gültig ab» = Wirkung, «erfahren am» = Wissen
/// (Walter 15.09.2026, TF33 Châtelain / docs/qst-korrektur-konzept.md Kap. 3).
/// Der Lohnlauf nimmt nur Versionen, die wir an diesem Stichtag schon kannten.
/// </summary>
public static class QstVersionWahl
{
    public static DateOnly BekanntAb(EmployeeQuellensteuer q)
        => q.ErfahrenAm ?? q.ValidFrom;

    /// <summary>
    /// Tarif, den wir am Stichtag kannten (Live-Lohnlauf). ValidTo zählt hier
    /// nicht: eine später erfahrene Version mit früherem Gültig-ab darf den
    /// alten Code in den Zwischenmonaten nicht verdrängen.
    /// ACHTUNG: QST-Korrektur-Posten nutzen NICHT diese Methode für «neu» —
    /// dort gilt die neue Version rückwirkend (Swissdec CompanyCorrection).
    /// </summary>
    public static EmployeeQuellensteuer? Waehle(
        IEnumerable<EmployeeQuellensteuer> alle, DateOnly stichtag)
        => alle
            .Where(q => q.ValidFrom <= stichtag && BekanntAb(q) <= stichtag)
            .OrderByDescending(q => q.ValidFrom)
            .ThenByDescending(q => BekanntAb(q))
            .ThenByDescending(q => q.Id)
            .FirstOrDefault();

    /// <summary>Letzter Monat, der den alten Code noch trug (Vormonat von Erfahren am).</summary>
    public static DateOnly? LetzterUnbekannterMonat(EmployeeQuellensteuer q)
    {
        var bekannt = new DateOnly(BekanntAb(q).Year, BekanntAb(q).Month, 1);
        var von = new DateOnly(q.ValidFrom.Year, q.ValidFrom.Month, 1);
        if (bekannt <= von) return null;
        return bekannt.AddMonths(-1);
    }
}
