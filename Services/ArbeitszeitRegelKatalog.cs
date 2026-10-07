using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

public static class ArbeitszeitEinstellungenLader
{
    /// <summary>Gültige Regeln einer Filiale: Vorlage ihres Hauptsitzes + eigene Abweichungen.</summary>
    public static async Task<ArbeitszeitEinstellungen> FuerFilialeAsync(AppDbContext db, int companyProfileId)
    {
        var hsId = await db.CompanyProfiles.AsNoTracking().Where(c => c.Id == companyProfileId)
            .Select(c => c.HauptsitzId).FirstOrDefaultAsync();
        var werte = await db.ArbeitszeitRegelWerte.AsNoTracking()
            .Where(w => w.CompanyProfileId == companyProfileId || (hsId != null && w.HauptsitzId == hsId))
            .ToListAsync();
        return ArbeitszeitEinstellungen.Aufloesen(
            werte.Where(w => w.HauptsitzId != null), werte.Where(w => w.CompanyProfileId != null));
    }
}

/// <summary>
/// Einstellbare Arbeitszeit-Regeln (Walter 07.10.2026). Standard = Gesetz/L-GAV; die
/// Vorlage hängt am Hauptsitz, jede Filiale kann einzelne Werte abweichend setzen.
/// Neue Regelarten brauchen Code in <see cref="ArbeitszeitVerstoesse"/> — hier stehen nur
/// die einstellbaren Werte.
/// </summary>
public static class ArbeitszeitRegelKatalog
{
    public const string Aktiv = "aktiv";
    public const string Text = "text";

    public record Parameter(string Schluessel, string Label, string Einheit, decimal Standard, decimal Min, decimal Max);
    public record Regel(string Code, string Titel, string Recht, IReadOnlyList<Parameter> Parameter);

    public static readonly IReadOnlyList<Regel> Alle = new List<Regel>
    {
        new(ArbeitszeitVerstoesse.Pause, "Pause zu kurz", "Art. 15 ArG", new Parameter[]
        {
            new("ab1_std", "Erste Stufe: mehr als", "Std.", 5.5m, 1, 24),
            new("pause1_min", "Pause erste Stufe", "Min.", 15, 0, 240),
            new("ab2_std", "Zweite Stufe: mehr als", "Std.", 7, 1, 24),
            new("pause2_min", "Pause zweite Stufe", "Min.", 30, 0, 240),
            new("ab3_std", "Dritte Stufe: mehr als", "Std.", 9, 1, 24),
            new("pause3_min", "Pause dritte Stufe", "Min.", 60, 0, 240),
            new("am_stueck_min", "Davon am Stück (dritte Stufe)", "Min.", 30, 0, 240),
            new("zaehlt_ab_min", "Unterbruch zählt als Pause ab", "Min.", 15, 1, 120),
        }),
        new(ArbeitszeitVerstoesse.Block, "Zu lange am Stück", "Art. 18 ArGV1", new Parameter[]
        {
            new("max_std", "Höchstens am Stück", "Std.", 5.5m, 1, 24),
        }),
        new(ArbeitszeitVerstoesse.NachtTag, "Nachtarbeit über 9 Std.", "Art. 17a ArG", new Parameter[]
        {
            new("max_std", "Höchstens pro Tag mit Nachtarbeit", "Std.", 9, 1, 24),
            new("nacht_von", "Nacht beginnt um", "Uhr", 23, 0, 24),
            new("nacht_bis", "Nacht endet um", "Uhr", 6, 0, 24),
        }),
        new(ArbeitszeitVerstoesse.Praesenz, "Präsenzzeit zu lang", "Art. 10 / 17a / 31 ArG", new Parameter[]
        {
            new("max_std", "Höchstens", "Std.", 14, 1, 24),
            new("max_nacht_std", "Höchstens mit Nachtarbeit", "Std.", 10, 1, 24),
            new("max_jugend_std", "Höchstens für Jugendliche", "Std.", 12, 1, 24),
        }),
        new(ArbeitszeitVerstoesse.Woche, "Über 50 Std. pro Woche", "Art. 9 ArG", new Parameter[]
        {
            new("max_std", "Höchstens pro Woche", "Std.", 50, 1, 100),
        }),
        new(ArbeitszeitVerstoesse.Ruhetage, "Weniger als 2 Ruhetage", "L-GAV Art. 15", new Parameter[]
        {
            new("min_tage", "Freie Tage pro Woche mindestens", "Tage", 2, 0, 7),
        }),
        new(ArbeitszeitVerstoesse.Naechte, "Zu viele Nächte", "Art. 30 ArGV1", Array.Empty<Parameter>()),
        new(ArbeitszeitVerstoesse.Jugend, "Jugendschutz", "Art. 31 ArG", new Parameter[]
        {
            new("max_std", "Höchstens pro Tag", "Std.", 9, 1, 24),
            new("ende_ab16", "Arbeitsende spätestens (ab 16)", "Uhr", 22, 0, 24),
            new("ende_unter16", "Arbeitsende spätestens (unter 16)", "Uhr", 20, 0, 24),
            new("beginn", "Arbeitsbeginn frühestens", "Uhr", 6, 0, 24),
        }),
        new(ArbeitszeitVerstoesse.SonntagJugend, "Jugendliche am Sonntag/Feiertag", "Art. 31 Abs. 4 ArG", new Parameter[]
        {
            new("sonntage", "Auch normale Sonntage prüfen", "ja/nein", 1, 0, 1),
        }),
    };

    public static Regel? Finde(string code) => Alle.FirstOrDefault(r => r.Code == code);

    public static bool SchluesselGueltig(string regel, string schluessel)
    {
        var r = Finde(regel);
        return r != null && (schluessel is Aktiv or Text || r.Parameter.Any(p => p.Schluessel == schluessel));
    }
}

/// <summary>Gültige Werte: Standard ← Vorlage (Hauptsitz) ← Filiale.</summary>
public sealed class ArbeitszeitEinstellungen
{
    readonly Dictionary<(string, string), decimal> _werte = new();
    readonly Dictionary<string, string> _texte = new();

    public static ArbeitszeitEinstellungen Gesetz { get; } = new();

    public static ArbeitszeitEinstellungen Aufloesen(IEnumerable<ArbeitszeitRegelWert> vorlage, IEnumerable<ArbeitszeitRegelWert> filiale)
    {
        var e = new ArbeitszeitEinstellungen();
        foreach (var w in vorlage.Concat(filiale))
        {
            if (!ArbeitszeitRegelKatalog.SchluesselGueltig(w.Regel, w.Schluessel)) continue;
            if (w.Schluessel == ArbeitszeitRegelKatalog.Text)
            {
                if (!string.IsNullOrWhiteSpace(w.Text)) e._texte[w.Regel] = w.Text.Trim();
            }
            else if (w.Wert.HasValue) e._werte[(w.Regel, w.Schluessel)] = w.Wert.Value;
        }
        return e;
    }

    public decimal Wert(string regel, string schluessel)
    {
        if (_werte.TryGetValue((regel, schluessel), out var v)) return v;
        return ArbeitszeitRegelKatalog.Finde(regel)?.Parameter.FirstOrDefault(p => p.Schluessel == schluessel)?.Standard
            ?? throw new ArgumentException($"Unbekannter Wert {regel}.{schluessel}");
    }

    public int Minuten(string regel, string schluessel) => (int)Math.Round(Wert(regel, schluessel) * 60);

    public bool IstAktiv(string regel) =>
        !_werte.TryGetValue((regel, ArbeitszeitRegelKatalog.Aktiv), out var v) || v != 0;

    public string? EigenerText(string regel) => _texte.TryGetValue(regel, out var t) ? t : null;
}
