namespace HrSystem.Services.Meldepflicht;

/// <summary>
/// E-Mail der kantonalen Behörde für Formular 2b (SEM), am Arbeitsort.
/// Quelle: Auswahlliste im offiziellen PDF, Stand der Vorlage.
/// </summary>
public static class MigrationsamtAdressen
{
    private static readonly Dictionary<string, string[]> NachKanton = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AG"] = new[] { "meldung.arbeit@ag.ch" },
        ["AI"] = new[] { "vpo_migration@jpmd.ai.ch", "thomas.signer@jpmd.ai.ch" },
        ["AR"] = new[] { "migration@ar.ch" },
        ["BE"] = new[] { "meldeverfahren.midi@be.ch" },
        ["BL"] = new[] { "vaflue.meldeverfahren@bl.ch" },
        ["BS"] = new[] { "va-flue.migrabs@bs.ch" },
        ["FR"] = new[] { "annonce.semo@fr.ch" },
        ["GE"] = new[] { "annonces-emploi.spar@etat.ge.ch" },
        ["GL"] = new[] { "mv-flue@gl.ch" },
        ["GR"] = new[] { "meldungerwerb@afm.gr.ch" },
        ["JU"] = new[] { "annonce.spop@jura.ch" },
        ["LU"] = new[] { "migration@lu.ch" },
        ["NE"] = new[] { "omoe@ne.ch" },
        ["NW"] = new[] { "meldungenva-flue@nw.ch" },
        ["OW"] = new[] { "migration@ow.ch" },
        ["SG"] = new[] { "erwerbsmeldung@sg.ch" },
        ["SH"] = new[] { "migrationsamt@ktsh.ch" },
        ["SO"] = new[] { "bewilligungen@ddi.so.ch" },
        ["SZ"] = new[] { "afa@sz.ch" },
        ["TG"] = new[] { "migrationsamt@tg.ch" },
        ["TI"] = new[] { "di-um.mercatodellavoro@ti.ch" },
        ["UR"] = new[] { "migration@ur.ch" },
        ["VD"] = new[] { "emploi.asile.spop@vd.ch" },
        ["VS"] = new[] { "sas-bip@admin.vs.ch" },
        ["ZG"] = new[] { "afm.erwerbfluechtlinge@zg.ch", "info.awa@zg.ch" },
        ["ZH"] = new[] { "meldeverfahrenvaflue@vd.zh.ch" },
    };

    public static IReadOnlyList<string> Fuer(string? kanton)
    {
        if (string.IsNullOrWhiteSpace(kanton)) return Array.Empty<string>();
        return NachKanton.TryGetValue(kanton.Trim(), out var liste) ? liste : Array.Empty<string>();
    }
}
