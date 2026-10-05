using HrSystem.Data;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Welche Absenz-Codes reduzieren welchen Saldo? Quelle ist der Katalog
/// (<c>absenz_typ.reduziert_saldo</c>, Walter 05.10.2026) — Lohnrechnung,
/// Saldo-Liste und Akonto lesen alle hier, damit sie nie auseinanderlaufen.
/// Fehlt ein aktiver Katalog-Eintrag, gilt der Standard (FERIEN/FEIERTAG).
/// </summary>
public static class AbsenzSaldoTypen
{
    public const string FerienTage   = "FERIEN_TAGE";
    public const string FeiertagTage = "FEIERTAG_TAGE";
    public const string NachtStunden = "NACHT_STUNDEN";

    public static string? Standard(string code) => code switch
    {
        "FERIEN"     => FerienTage,
        "FEIERTAG"   => FeiertagTage,
        "NACHT_KOMP" => NachtStunden,
        _            => null,
    };

    public static HashSet<string> Codes(IEnumerable<(string Code, string? ReduziertSaldo)> aktiveTypen, string saldo)
    {
        var liste = aktiveTypen.ToList();
        var set = liste.Where(t => t.ReduziertSaldo == saldo).Select(t => t.Code).ToHashSet();
        foreach (var code in new[] { "FERIEN", "FEIERTAG", "NACHT_KOMP" })
            if (Standard(code) == saldo && !liste.Any(t => t.Code == code))
                set.Add(code);
        return set;
    }

    public static async Task<HashSet<string>> CodesAsync(AppDbContext db, string saldo)
    {
        var aktiv = await db.AbsenzTypen.AsNoTracking()
            .Where(t => t.Aktiv)
            .Select(t => new { t.Code, t.ReduziertSaldo })
            .ToListAsync();
        return Codes(aktiv.Select(t => (t.Code, t.ReduziertSaldo)), saldo);
    }
}
