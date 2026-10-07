using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Einmalige Korrektur der Feiertage aus dem Seed vom 09.08.2026 (Walter 07.10.2026):
/// Berchtoldstag, Ostermontag und Pfingstmontag waren «national» erfasst, gelten aber nicht in
/// allen Kantonen gleich (LU: kein Berchtoldstag, Ostermontag/Pfingstmontag nicht sonntagsgleich).
/// Solche Tage werden auf Kantons-Einträge verteilt; Tage, die laut Kantonsliste dem Sonntag
/// gleichgestellt sind, bekommen das Häkchen. Setzt nur «wie Sonntag» dazu, nimmt nie eines weg.
/// Läuft genau einmal (beim Wechsel auf Schema-Stand 55).
/// </summary>
public static class FeiertagAltbestand
{
    public record Ergebnis(int Sonntagsgleich, int Aufgeteilt, int KantonNeu, bool Uebersprungen, string? Grund);

    public static async Task<Ergebnis> KantonslistenAnwendenAsync(AppDbContext db)
    {
        var kantone = (await db.CompanyProfiles.AsNoTracking()
                .Where(c => c.KantonCode != null && c.KantonCode != "")
                .Select(c => c.KantonCode!).ToListAsync())
            .Select(k => k.Trim().ToUpperInvariant()).Distinct().OrderBy(k => k).ToList();
        var kantonVonFiliale = (await db.CompanyProfiles.AsNoTracking()
                .Select(c => new { c.Id, c.KantonCode }).ToListAsync())
            .ToDictionary(c => c.Id, c => c.KantonCode?.Trim().ToUpperInvariant());

        var alle = await db.DienstplanFeiertage.ToListAsync();
        int sonntag = 0, aufgeteilt = 0, neu = 0;

        bool alleKantoneBekannt = kantone.Count > 0 && kantone.All(FeiertagVorschlag.KantonslisteVorhanden);
        foreach (var f in alle.Where(x => x.Scope == "NATIONAL" && !(x.Datum.Month == 8 && x.Datum.Day == 1)).ToList())
        {
            if (!alleKantoneBekannt) break;
            var stufen = kantone.ToDictionary(k => k, k => FeiertagVorschlag.Einstufen(k, f.Datum));
            if (stufen.Values.Any(s => s == FeiertagVorschlag.Einstufung.Unbekannt)) continue;
            if (stufen.Values.All(s => s == FeiertagVorschlag.Einstufung.Sonntag))
            {
                if (!f.Sonntagsgleich) { f.Sonntagsgleich = true; sonntag++; }
                continue;
            }
            if (stufen.Values.Distinct().Count() == 1) continue;
            foreach (var (k, s) in stufen)
            {
                if (s == FeiertagVorschlag.Einstufung.Kein) continue;
                var vorhanden = alle.FirstOrDefault(x => x.Scope == "KANTON" && x.KantonCode == k && x.Datum == f.Datum);
                if (vorhanden != null)
                {
                    if (s == FeiertagVorschlag.Einstufung.Sonntag && !vorhanden.Sonntagsgleich) { vorhanden.Sonntagsgleich = true; sonntag++; }
                    continue;
                }
                var e = new DienstplanFeiertag
                {
                    Datum = f.Datum, Bezeichnung = f.Bezeichnung, Scope = "KANTON", KantonCode = k,
                    Sonntagsgleich = s == FeiertagVorschlag.Einstufung.Sonntag, CreatedAt = DateTime.Now,
                };
                db.DienstplanFeiertage.Add(e);
                alle.Add(e);
                neu++;
            }
            db.DienstplanFeiertage.Remove(f);
            aufgeteilt++;
        }

        foreach (var f in alle.Where(x => x.Scope is "KANTON" or "FILIALE" && !x.Sonntagsgleich))
        {
            var kanton = f.Scope == "KANTON" ? f.KantonCode
                : f.CompanyProfileId is int cp && kantonVonFiliale.TryGetValue(cp, out var kc) ? kc : null;
            if (FeiertagVorschlag.Einstufen(kanton, f.Datum) == FeiertagVorschlag.Einstufung.Sonntag)
            {
                f.Sonntagsgleich = true;
                sonntag++;
            }
        }

        await db.SaveChangesAsync();
        return new Ergebnis(sonntag, aufgeteilt, neu, !alleKantoneBekannt,
            alleKantoneBekannt ? null : $"Nationale Einträge nicht angepasst — Kantone ohne hinterlegte Liste: {string.Join(", ", kantone.Where(k => !FeiertagVorschlag.KantonslisteVorhanden(k)))}");
    }
}
