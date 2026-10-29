namespace HrSystem.Services;

/// <summary>
/// «Gekündigt per» setzt das Vertragsende. easy@work bleibt die Quelle für das
/// Ende, darf ein so gesetztes Datum aber nicht wieder öffnen oder nach hinten
/// schieben. Die Vertragsart bleibt unbefristet — der Austritt ist kein
/// befristeter Vertrag.
/// </summary>
public static class KuendigungVertragsende
{
    public static bool Deckt(DateTime beginn, DateTime? ende, DateTime per)
    {
        var tag = per.Date;
        return beginn.Date <= tag && (ende == null || ende.Value.Date >= tag);
    }

    /// <summary>
    /// Ende, das gespeichert wird. Liegt der Vertragsbeginn nach der Kündigung,
    /// gilt easy@work unverändert. Sonst nie später als «gekündigt per».
    /// </summary>
    public static DateTime? Begrenze(DateTime? easyEnde, DateTime vertragsbeginn, DateTime? kuendigungPer)
    {
        if (kuendigungPer == null) return easyEnde;
        var per = kuendigungPer.Value.Date;
        if (vertragsbeginn.Date > per) return easyEnde;
        if (easyEnde == null || easyEnde.Value.Date > per) return per;
        return easyEnde;
    }
}
