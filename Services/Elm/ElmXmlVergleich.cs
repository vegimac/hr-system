using System.Text;
using System.Xml.Linq;

namespace HrSystem.Services.Elm;

/// <summary>
/// Vergleicht eine erzeugte ELM-Meldung Feld für Feld mit einer Referenz-Meldung
/// von Swissdec (Walter 27.09.2026). Personen werden über die Personalnummer
/// zugeordnet, nicht über die Reihenfolge — die Referenz sortiert anders als wir.
///
/// <para>Der Vergleich ist ein WERKZEUG, kein Urteil: Unterschiede, die im
/// Abweichungsprotokoll als bewusst festgehalten sind (A5, A7, F4, TF29 …),
/// werden als «bewusst» markiert und nicht als Fehler gezählt. Nie einen Wert
/// aus der Referenz abschreiben, um grün zu werden.</para>
/// </summary>
public static class ElmXmlVergleich
{
    public record Unterschied(string Person, string Feld, string? Ist, string? Soll, string? Bewusst)
    {
        public bool IstBewusst => !string.IsNullOrEmpty(Bewusst);
    }

    public record Ergebnis(List<Unterschied> Unterschiede, int Geprueft, int PersonenIst, int PersonenSoll)
    {
        public int Offen => Unterschiede.Count(u => !u.IstBewusst);
        public int Bewusst => Unterschiede.Count(u => u.IstBewusst);
    }

    private static readonly XNamespace C  = ElmGemeinsam.C;
    private static readonly XNamespace Sd = ElmGemeinsam.Sd;

    /// <summary>
    /// Bekannte, bewusste Abweichungen. Schlüssel = Feldpfad (Ende), Wert = Kürzel
    /// im Abweichungsprotokoll. Die Liste ist bewusst kurz und explizit: was hier
    /// nicht steht, ist offen und muss angeschaut werden.
    /// </summary>
    private static readonly (string FeldEnde, string Kuerzel, string Grund)[] BewussteFelder =
    {
        ("MonthlyValues/SocialContributions", "A5",
            "Statistiksumme auf 5 Rappen gerundet — unsere Belege sind rappengenau"),
    };

    private static string? BewusstFuer(string feld, string? ist, string? soll)
    {
        foreach (var (ende, kuerzel, grund) in BewussteFelder)
        {
            if (!feld.EndsWith(ende, StringComparison.Ordinal)) continue;
            // Nur als bewusst durchgehen lassen, solange es um Rappen geht.
            if (decimal.TryParse(ist, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var a)
                && decimal.TryParse(soll, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var b)
                && Math.Abs(a - b) <= 0.05m)
                return $"{kuerzel}: {grund}";
        }
        return null;
    }

    /// <summary>Alle Blattwerte eines Elements als Pfad → Wert (mehrfache Pfade durchnummeriert).</summary>
    private static Dictionary<string, string> Blaetter(XElement wurzel, string prefix = "")
    {
        var res = new Dictionary<string, string>(StringComparer.Ordinal);
        void Lauf(XElement el, string pfad)
        {
            var kinder = el.Elements().ToList();
            if (kinder.Count == 0)
            {
                var wert = (el.Value ?? "").Trim();
                var p = pfad;
                int i = 2;
                while (res.ContainsKey(p)) p = $"{pfad}[{i++}]";
                res[p] = wert;
                return;
            }
            var zaehler = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var k in kinder)
            {
                var name = k.Name.LocalName;
                zaehler[name] = zaehler.TryGetValue(name, out var v) ? v + 1 : 1;
                var gleiche = kinder.Count(x => x.Name.LocalName == name);
                var teil = gleiche > 1 ? $"{name}#{zaehler[name]}" : name;
                Lauf(k, pfad.Length == 0 ? teil : $"{pfad}/{teil}");
            }
        }
        Lauf(wurzel, prefix);
        return res;
    }

    /// <summary>Personalnummer einer Person im XML.</summary>
    private static string PersNr(XElement person)
        => person.Descendants(C + "EmployeeNumber").FirstOrDefault()?.Value.Trim() ?? "?";

    private static string Name(XElement person)
    {
        var v = person.Descendants(C + "Firstname").FirstOrDefault()?.Value.Trim();
        var n = person.Descendants(C + "Lastname").FirstOrDefault()?.Value.Trim();
        return $"{v} {n}".Trim();
    }

    /// <summary>
    /// Vergleicht zwei Meldungen. Felder, die naturgemäss abweichen (Zeitstempel,
    /// RequestID, Hersteller), bleiben aussen vor — sie sagen nichts über die Rechnung.
    /// </summary>
    public static Ergebnis Vergleiche(string istXml, string sollXml)
    {
        var ist = XDocument.Parse(istXml).Root!;
        var soll = XDocument.Parse(sollXml).Root!;
        var unterschiede = new List<Unterschied>();
        int geprueft = 0;

        bool Ueberspringen(string feld) =>
            feld.Contains("RequestContext") || feld.Contains("TransmissionDate")
            || feld.Contains("RequestID") || feld.Contains("CreationDate")
            || feld.Contains("ContactPerson");

        // ── Firmenteil ───────────────────────────────────────────────────────
        var istFirma = ist.Descendants(Sd + "CompanyDescription").FirstOrDefault();
        var sollFirma = soll.Descendants(Sd + "CompanyDescription").FirstOrDefault();
        if (istFirma != null && sollFirma != null)
        {
            var a = Blaetter(istFirma, "CompanyDescription");
            var b = Blaetter(sollFirma, "CompanyDescription");
            foreach (var feld in a.Keys.Union(b.Keys).OrderBy(x => x, StringComparer.Ordinal))
            {
                if (Ueberspringen(feld)) continue;
                a.TryGetValue(feld, out var av); b.TryGetValue(feld, out var bv);
                geprueft++;
                if (av != bv) unterschiede.Add(new Unterschied("Firma", feld, av, bv, BewusstFuer(feld, av, bv)));
            }
        }

        // ── Personen über die Personalnummer zuordnen ────────────────────────
        var istPers = ist.Descendants(Sd + "Person").ToDictionary(PersNr, p => p, StringComparer.Ordinal);
        var sollPers = soll.Descendants(Sd + "Person").ToDictionary(PersNr, p => p, StringComparer.Ordinal);

        foreach (var nr in istPers.Keys.Union(sollPers.Keys).OrderBy(x => x, StringComparer.Ordinal))
        {
            istPers.TryGetValue(nr, out var pi);
            sollPers.TryGetValue(nr, out var ps);
            var label = $"Nr. {nr}" + (ps != null ? $" {Name(ps)}" : pi != null ? $" {Name(pi)}" : "");
            if (pi == null) { unterschiede.Add(new Unterschied(label, "Person", "fehlt", "vorhanden", null)); continue; }
            if (ps == null) { unterschiede.Add(new Unterschied(label, "Person", "vorhanden", "fehlt", null)); continue; }

            var a = Blaetter(pi, "Person");
            var b = Blaetter(ps, "Person");
            foreach (var feld in a.Keys.Union(b.Keys).OrderBy(x => x, StringComparer.Ordinal))
            {
                if (Ueberspringen(feld)) continue;
                a.TryGetValue(feld, out var av); b.TryGetValue(feld, out var bv);
                geprueft++;
                if (av != bv) unterschiede.Add(new Unterschied(label, feld, av, bv, BewusstFuer(feld, av, bv)));
            }
        }

        // ── Summen und Zähler ───────────────────────────────────────────────
        foreach (var block in new[] { "SalaryTotals", "SalaryCounters", "Institutions" })
        {
            var ai = ist.Descendants(Sd + block).FirstOrDefault();
            var bi = soll.Descendants(Sd + block).FirstOrDefault();
            if (ai == null && bi == null) continue;
            var a = ai == null ? new Dictionary<string, string>() : Blaetter(ai, block);
            var b = bi == null ? new Dictionary<string, string>() : Blaetter(bi, block);
            foreach (var feld in a.Keys.Union(b.Keys).OrderBy(x => x, StringComparer.Ordinal))
            {
                a.TryGetValue(feld, out var av); b.TryGetValue(feld, out var bv);
                geprueft++;
                if (av != bv) unterschiede.Add(new Unterschied("Meldung", feld, av, bv, BewusstFuer(feld, av, bv)));
            }
        }

        return new Ergebnis(unterschiede, geprueft, istPers.Count, sollPers.Count);
    }

    /// <summary>Bericht als Markdown — Grundlage für SWISSCEC/Abgleich/elm_*.md.</summary>
    public static string Bericht(Ergebnis e, string titel, string referenzDatei)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {titel}");
        sb.AppendLine();
        sb.AppendLine($"Referenz: `{referenzDatei}`");
        sb.AppendLine();
        sb.AppendLine($"- Personen in unserer Meldung: **{e.PersonenIst}**, in der Referenz: **{e.PersonenSoll}**");
        sb.AppendLine($"- Verglichene Felder: **{e.Geprueft}**");
        sb.AppendLine($"- Offene Unterschiede: **{e.Offen}**");
        sb.AppendLine($"- Bewusste Abweichungen: **{e.Bewusst}**");
        sb.AppendLine();
        if (e.Unterschiede.Count == 0)
        {
            sb.AppendLine("Kein Unterschied — die Meldung deckt sich Feld für Feld mit der Referenz.");
            return sb.ToString();
        }
        sb.AppendLine("| Person | Feld | OneCrew | Referenz | |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var u in e.Unterschiede.OrderBy(x => x.IstBewusst).ThenBy(x => x.Person, StringComparer.Ordinal).ThenBy(x => x.Feld, StringComparer.Ordinal))
            sb.AppendLine($"| {u.Person} | `{u.Feld}` | {u.Ist ?? "—"} | {u.Soll ?? "—"} | {(u.IstBewusst ? u.Bewusst : "**offen**")} |");
        return sb.ToString();
    }
}
