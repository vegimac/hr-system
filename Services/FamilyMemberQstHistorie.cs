using HrSystem.Models;

namespace HrSystem.Services;

/// <summary>
/// Stichtag-Auflösung Bewilligung / Erwerb eines Familienangehörigen
/// (Walter 08.10.2026) — Wirkung = ValidFrom, Wissen = ErfahrenAm ?? ValidFrom.
/// </summary>
public static class FamilyMemberQstHistorie
{
    public static DateOnly BekanntAb(DateOnly validFrom, DateOnly? erfahrenAm)
        => erfahrenAm ?? validFrom;

    /// <summary>Letzter Bewilligungs-Eintrag, der am Stichtag bekannt war.</summary>
    public static FamilyMemberPermitHistory? PermitAm(
        IEnumerable<FamilyMemberPermitHistory> hist, DateOnly stichtag)
    {
        FamilyMemberPermitHistory? treffer = null;
        foreach (var h in hist.OrderBy(x => x.ValidFrom).ThenBy(x => x.Id))
        {
            if (h.ValidFrom <= stichtag && BekanntAb(h.ValidFrom, h.ErfahrenAm) <= stichtag)
                treffer = h;
        }
        return treffer;
    }

    /// <summary>Letzter Erwerbs-Eintrag, der am Stichtag bekannt war.</summary>
    public static FamilyMemberErwerbHistory? ErwerbAm(
        IEnumerable<FamilyMemberErwerbHistory> hist, DateOnly stichtag)
    {
        FamilyMemberErwerbHistory? treffer = null;
        foreach (var h in hist.OrderBy(x => x.ValidFrom).ThenBy(x => x.Id))
        {
            if (h.ValidFrom <= stichtag && BekanntAb(h.ValidFrom, h.ErfahrenAm) <= stichtag)
                treffer = h;
        }
        return treffer;
    }

    /// <summary>
    /// C-Ausweis am Stichtag? Ablauf zählt administrativ (wie MA-Permit);
    /// Wissen/Wirkung über Historie. Ohne Historie: Snapshot-Fallback.
    /// </summary>
    public static bool SpouseHatCAmStichtag(
        EmployeeFamilyMember spouse,
        IReadOnlyList<FamilyMemberPermitHistory> hist,
        DateOnly stichtag,
        Func<string?, bool> istC)
    {
        if (hist.Count > 0)
        {
            var am = PermitAm(hist, stichtag);
            if (am == null) return false;
            if (!istC(am.PermitType?.Code)) return false;
            return am.ValidTo == null || am.ValidTo.Value >= stichtag;
        }
        // Altbestand ohne Historie
        if (!istC(spouse.PermitType?.Code)) return false;
        if (spouse.PermitExpiryDate == null) return true;
        return DateOnly.FromDateTime(spouse.PermitExpiryDate.Value) >= stichtag;
    }

    /// <summary>Erwerbstätig am Stichtag; ohne Historie Snapshot.</summary>
    public static bool? ErwerbstaetigAmStichtag(
        EmployeeFamilyMember spouse,
        IReadOnlyList<FamilyMemberErwerbHistory> hist,
        DateOnly stichtag)
    {
        if (hist.Count > 0)
            return ErwerbAm(hist, stichtag)?.Erwerbstaetig;
        return spouse.Erwerbstaetig;
    }
}
