using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services.LohnFormelwerk;

/// <summary>
/// Lohnschema-API / UI-Übersicht. Die Lohn-Engine nutzt dieses Schema nicht mehr
/// (Walter 19.10.2026) — Rechnung fest im Code, Steuerung über Positions-Flags.
/// </summary>
public sealed class LohnSchemaSteuerung
{
    private readonly AppDbContext _db;
    private Dictionary<string, HashSet<string>>? _cache; // "FLEX|automatisch" → codes

    public LohnSchemaSteuerung(AppDbContext db) => _db = db;

    public async Task LadenAsync(CancellationToken ct = default)
    {
        var rows = await _db.Set<VertragsmodellLohnschema>()
            .AsNoTracking()
            .Include(s => s.Lohnposition)
            .Where(s => s.Lohnposition != null && s.Lohnposition.IsActive)
            .ToListAsync(ct);

        var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows)
        {
            var code = r.Lohnposition!.Code;
            foreach (var modell in Modelle(r.Modell))
            {
                var key = Key(modell, r.Art);
                if (!map.TryGetValue(key, out var set))
                {
                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    map[key] = set;
                }
                set.Add(code);
                set.Add(LohnCodeAlias.Normiere(code));
                var mirus = r.Lohnposition.MirusCode;
                if (!string.IsNullOrWhiteSpace(mirus)) set.Add(mirus);
            }
        }
        _cache = map;
    }

    /// <summary>
    /// Darf die Engine diese Position für Modell/Art schreiben?
    /// Kein Schema für dieses Modell → alles erlaubt (Übergang).
    /// Sobald mind. ein Schema-Eintrag für das Modell existiert: nur gelistete Codes.
    /// </summary>
    public bool IstErlaubt(string? modell, string art, string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        if (_cache == null || _cache.Count == 0) return true;

        var n = LohnCodeAlias.Normiere(code);
        var m = (modell ?? "").Trim().ToUpperInvariant();
        if (m is "UTP") m = "FLEX";

        bool hatSchemaFuerModell = _cache.Keys.Any(k =>
            k.StartsWith(m + "|", StringComparison.OrdinalIgnoreCase)
            || k.StartsWith("ALLE|", StringComparison.OrdinalIgnoreCase));
        if (!hatSchemaFuerModell) return true; // Modell noch nicht im Schema → wie bisher

        foreach (var a in new[] { art, "automatisch", "saldo", "ereignis", "austritt", "manuell" })
        {
            if (_cache.TryGetValue(Key(m, a), out var set)
                && (set.Contains(code!) || set.Contains(n)))
                return true;
            if (_cache.TryGetValue(Key("ALLE", a), out var alle)
                && (alle.Contains(code!) || alle.Contains(n)))
                return true;
        }
        return false;
    }

    /// <summary>Codes die laut Schema für Modell+Art fällig sind (Engine/UI).</summary>
    public IReadOnlyCollection<string> FaelligeCodes(string? modell, string art)
    {
        if (_cache == null) return Array.Empty<string>();
        var m = (modell ?? "").Trim().ToUpperInvariant();
        if (m is "UTP") m = "FLEX";
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_cache.TryGetValue(Key(m, art), out var a)) foreach (var c in a) set.Add(c);
        if (_cache.TryGetValue(Key("ALLE", art), out var b)) foreach (var c in b) set.Add(c);
        return set;
    }

    /// <summary>Systemzeilen (SV/QST) brauchen keinen Schema-Eintrag.</summary>
    public static bool IstSystemzeile(string? categoryCode) =>
        !string.IsNullOrWhiteSpace(categoryCode);

    private static string Key(string modell, string art) =>
        $"{modell.Trim().ToUpperInvariant()}|{art.Trim().ToLowerInvariant()}";

    private static IEnumerable<string> Modelle(string modell)
    {
        var m = (modell ?? "").Trim().ToUpperInvariant();
        if (m is "ALLE" or "*") { yield return "FLEX"; yield return "MTP"; yield return "FIX"; yield return "FIX-M"; yield return "ALLE"; yield break; }
        if (m is "UTP") m = "FLEX";
        yield return m;
    }
}
