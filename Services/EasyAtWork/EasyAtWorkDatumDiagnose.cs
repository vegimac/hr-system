using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HrSystem.Services.EasyAtWork;

/// <summary>
/// Datums-Diagnose (Walter-Vorgabe 24.09.2026, «ein für allemal lösen»):
/// easy@work speichert ein UI-Datum als UTC-Zeitpunkt, und zwar je nach Objekt
/// und Eingabeweg als Tagesanfang, Tagesende oder exklusive Mitternacht. Diese
/// Klasse schreibt NICHTS — sie ordnet jeden Roh-Wert einer Speicherart zu und
/// meldet Stellen, an denen die Umrechnung einen Tag danebenliegen könnte
/// (Fälle 1220009 und 580101). Grundlage für die zentrale Umrechnung und die
/// Support-Anfrage bei easy@work.
/// </summary>
public static class EasyAtWorkDatumDiagnose
{
    public const string NurDatum     = "nur Datum";
    public const string Tagesanfang  = "00:00 Zürich (Tagesanfang)";
    public const string Tagesende    = "23:59:59 Zürich (Tagesende)";
    public const string AndereZeit   = "andere Uhrzeit";
    public const string Unlesbar     = "unlesbar";

    public record Einordnung(string Art, DateTime? Lokal, bool Sommerzeit);

    public record Befund(string Code, string Text, string? Roh = null);

    /// <summary>Ein Roh-Wert und seine Zuordnung — Material für die Regeltabelle.</summary>
    public record Beispiel(string Feld, string Art, string Roh, string Lokal, string Gelesen, bool Sommerzeit);

    public record MaErgebnis(List<Befund> Befunde, List<Beispiel> Werte);

    /// <summary>Ordnet einen easy@work-Roh-Wert einer Speicherart zu.</summary>
    public static Einordnung Einordnen(string? roh)
    {
        if (string.IsNullOrWhiteSpace(roh)) return new(Unlesbar, null, false);
        var s = roh.Trim();
        if (DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return new(NurDatum, d.ToDateTime(TimeOnly.MinValue), false);
        var lokal = EawDateUtil.ToSwissLocal(s);
        if (lokal == null) return new(Unlesbar, null, false);
        var (zeit, sommer) = lokal.Value;
        var t = zeit.TimeOfDay;
        string art = t == TimeSpan.Zero ? Tagesanfang
                   : t == new TimeSpan(23, 59, 59) ? Tagesende
                   : AndereZeit;
        return new(art, zeit, sommer);
    }

    /// <summary>
    /// Prüft die Verträge und Lohnsätze EINES MA. Gelöschte Einträge werden wie
    /// im Sync übergangen. Die Befunde beziehen sich auf die Rohdaten VOR den
    /// Korrekturen des Syncs; ob eine Korrektur greift, steht im Befundtext.
    /// </summary>
    public static MaErgebnis Pruefe(List<EawContract>? contracts, List<EawPayRate>? rates)
    {
        var befunde = new List<Befund>();
        var werte = new List<Beispiel>();
        var cs = (contracts ?? new()).Where(c => !c.IsDeleted).OrderBy(c => c.From ?? DateOnly.MinValue).ToList();
        var rs = (rates ?? new()).Where(r => !r.IsDeleted).OrderBy(r => r.From ?? DateOnly.MinValue).ToList();

        void Erfasse(string feld, string? roh, DateOnly? gelesen)
        {
            if (string.IsNullOrWhiteSpace(roh)) return;
            var e = Einordnen(roh);
            werte.Add(new Beispiel(feld, e.Art, roh.Trim(),
                e.Lokal?.ToString("dd.MM.yyyy HH:mm:ss") ?? "–",
                gelesen?.ToString("dd.MM.yyyy") ?? "–", e.Sommerzeit));
            if (e.Art == AndereZeit)
                befunde.Add(new("UHRZEIT", $"{feld}: Uhrzeit {e.Lokal:HH:mm:ss} Zürich ist weder Tagesanfang noch Tagesende — Kalendertag unsicher.", roh));
            if (e.Art == Unlesbar)
                befunde.Add(new("UNLESBAR", $"{feld}: Wert nicht lesbar.", roh));
        }

        foreach (var c in cs) { Erfasse("Vertrag von", c.FromRaw, c.From); Erfasse("Vertrag bis", c.ToRaw, c.To); }
        foreach (var r in rs) { Erfasse("Lohnsatz von", r.FromRaw, r.From); Erfasse("Lohnsatz bis", r.ToRaw, r.To); }

        // 1) Vertrag, der nach dem Einlesen nur einen Tag dauert (Fall 580101).
        foreach (var c in cs.Where(c => c.From.HasValue && c.To.HasValue && c.To.Value <= c.From.Value))
            befunde.Add(new("VERTRAG_EIN_TAG",
                $"Vertrag {c.From:dd.MM.yyyy}–{c.To:dd.MM.yyyy} dauert höchstens einen Tag — vermutlich ein Umrechnungs-Rest.",
                $"from={c.FromRaw} · to={c.ToRaw}"));

        // 2) Aufeinanderfolgende Verträge mit einem Tag Lücke oder Überlappung.
        for (int i = 1; i < cs.Count; i++)
        {
            var a = cs[i - 1]; var b = cs[i];
            if (!a.To.HasValue || !b.From.HasValue) continue;
            int diff = b.From.Value.DayNumber - a.To.Value.DayNumber;   // 1 = sauber anschliessend
            if (diff == 0)
                befunde.Add(new("VERTRAG_UEBERLAPPT_1_TAG",
                    $"Vertrag bis {a.To:dd.MM.yyyy} und Folgevertrag ab {b.From:dd.MM.yyyy} überlappen um einen Tag.",
                    $"bis={a.ToRaw} · ab={b.FromRaw}"));
            else if (diff == 2)
                befunde.Add(new("VERTRAG_LUECKE_1_TAG",
                    $"Zwischen Vertrag bis {a.To:dd.MM.yyyy} und Folgevertrag ab {b.From:dd.MM.yyyy} fehlt genau ein Tag.",
                    $"bis={a.ToRaw} · ab={b.FromRaw}"));
        }

        // 3) Lohnsatz endet einen Tag vor/nach dem Vertrag, ohne Nachfolge-Lohnsatz.
        foreach (var r in rs.Where(r => r.To.HasValue))
        {
            var rTo = r.To!.Value;
            bool nachfolger = rs.Any(x => !ReferenceEquals(x, r) && x.From.HasValue
                && x.From.Value <= rTo.AddDays(1) && (!x.To.HasValue || x.To.Value > rTo));
            if (nachfolger) continue;
            foreach (var c in cs.Where(c => c.To.HasValue && c.From.HasValue && c.From.Value <= rTo))
            {
                int d = c.To!.Value.DayNumber - rTo.DayNumber;
                if (d == 1)
                    befunde.Add(new("LOHNSATZ_ENDE_1_TAG_FRUEHER",
                        $"Lohnsatz endet {rTo:dd.MM.yyyy}, Vertrag {c.To:dd.MM.yyyy} — der Sync gleicht das seit 24.09.2026 automatisch an.",
                        $"Lohnsatz to={r.ToRaw} · Vertrag to={c.ToRaw}"));
                else if (d == -1)
                    befunde.Add(new("LOHNSATZ_ENDE_1_TAG_SPAETER",
                        $"Lohnsatz endet {rTo:dd.MM.yyyy}, Vertrag schon {c.To:dd.MM.yyyy} — nicht automatisch angeglichen.",
                        $"Lohnsatz to={r.ToRaw} · Vertrag to={c.ToRaw}"));
            }
        }

        // 4) Vertragsbeginn ohne Lohnsatz am selben Tag, aber einer einen Tag daneben.
        foreach (var c in cs.Where(c => c.From.HasValue))
        {
            var f = c.From!.Value;
            if (rs.Any(r => r.From == f)) continue;
            var nah = rs.FirstOrDefault(r => r.From.HasValue && Math.Abs(r.From.Value.DayNumber - f.DayNumber) == 1);
            if (nah != null)
                befunde.Add(new("LOHNSATZ_BEGINN_1_TAG_DANEBEN",
                    $"Vertrag beginnt {f:dd.MM.yyyy}, Lohnsatz {nah.From:dd.MM.yyyy}.",
                    $"Vertrag from={c.FromRaw} · Lohnsatz from={nah.FromRaw}"));
        }

        return new MaErgebnis(befunde, werte);
    }
}
