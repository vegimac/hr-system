using System.Globalization;
using System.Text.RegularExpressions;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace HrSystem.Services;

/// <summary>
/// HR-Bereich → GastroSocial → «BVG-Abgleich: Versichertenliste» (Walter 31.10.2026).
///
/// Excel «Versichertenliste - Auskunft» der beruflichen Vorsorge
/// (Name, Vorname, AHV-Nummer, Geburtsdatum, Bezeichnung, Gültig ab, Gültig bis)
/// gegen die BVG-Versicherungspflicht der gewählten Filiale.
/// Wer nicht auf der Liste steht, wird nicht auf «nicht versichert» gesetzt.
/// Die Bezeichnung (Uno Basis / Uno Integral) ist der Plan und wird nur mitgeschrieben,
/// nicht als Versicherungscode übernommen.
/// </summary>
public static class BvgVersichertenAbgleich
{
    public const string Anlegen = "anlegen";
    public const string Beenden = "beenden";
    /// <summary>Offener Eintrag, OneCrew beginnt später als die Liste: «Gültig ab» wird auf das Datum der Liste gesetzt.</summary>
    public const string Datum = "datum";

    public class Zeile
    {
        public string? Nachname { get; set; }
        public string? Vorname { get; set; }
        public string? Ahv { get; set; }
        public DateTime? Geburtsdatum { get; set; }
        public string? Bezeichnung { get; set; }
        public DateTime? GueltigAb { get; set; }
        public DateTime? GueltigBis { get; set; }
    }

    /// <summary>OneCrew-MA mit dem heute gültigen Eintrag «versichert», falls vorhanden.</summary>
    public class Ma
    {
        public int Id { get; set; }
        public string? Nummer { get; set; }
        public string? Vorname { get; set; }
        public string? Nachname { get; set; }
        public string? Ahv { get; set; }
        public DateTime? Geburtsdatum { get; set; }
        public bool InFiliale { get; set; }
        public bool AktivInFiliale { get; set; }
        public bool OhneLohn { get; set; }
        public string? AndereFiliale { get; set; }
        public DateOnly? VersichertAb { get; set; }
        public DateOnly? VersichertBis { get; set; }
    }

    public class Treffer
    {
        public int? EmployeeId { get; set; }
        public string? Vorname { get; set; }
        public string? Name { get; set; }
        public string? OneCrewName { get; set; }
        public string? Ahv { get; set; }
        public string? Personalnummer { get; set; }
        public string? Bezeichnung { get; set; }
        public DateTime? Geburtsdatum { get; set; }
        public DateTime? GueltigAb { get; set; }
        public DateTime? GueltigBis { get; set; }
        public DateTime? OneCrewAb { get; set; }
        public DateTime? OneCrewBis { get; set; }
        public string? Hinweis { get; set; }
        public string? Aktion { get; set; }
    }

    public class Ergebnis
    {
        public List<Treffer> Passt { get; set; } = new();
        public List<Treffer> Festlegen { get; set; } = new();
        public List<Treffer> Beenden { get; set; } = new();
        public List<Treffer> Abweichend { get; set; } = new();
        public List<Treffer> NichtZuordenbar { get; set; } = new();
        public List<Treffer> AndereFiliale { get; set; } = new();
        public List<Treffer> NurInOneCrew { get; set; } = new();
    }

    public static Ergebnis Vergleiche(IEnumerable<Zeile> zeilen, IEnumerable<Ma> mitarbeiter, DateTime heute)
    {
        var alle = mitarbeiter.ToList();
        var res = new Ergebnis();
        var gesehen = new HashSet<int>();
        foreach (var z in zeilen)
        {
            var ab = z.GueltigAb?.Date;
            if (ab == null)
            {
                res.NichtZuordenbar.Add(Basis(z, null, "«Gültig ab» fehlt."));
                continue;
            }
            if (z.GueltigBis != null && z.GueltigBis.Value.Date < ab)
            {
                res.NichtZuordenbar.Add(Basis(z, null, "«Gültig bis» liegt vor «Gültig ab»."));
                continue;
            }
            var hit = Finde(z, alle);
            if (hit == null)
            {
                res.NichtZuordenbar.Add(Basis(z, null, "In OneCrew nicht gefunden."));
                continue;
            }
            if (!hit.InFiliale)
            {
                var wo = string.IsNullOrWhiteSpace(hit.AndereFiliale) ? "einer anderen Filiale" : hit.AndereFiliale;
                res.AndereFiliale.Add(Basis(z, hit, $"Arbeitet in {wo}. Filiale wechseln und die Liste dort nochmals vergleichen."));
                continue;
            }
            if (!gesehen.Add(hit.Id)) continue;

            var listAb = DateOnly.FromDateTime(ab.Value);
            var listBis = z.GueltigBis == null ? (DateOnly?)null : DateOnly.FromDateTime(z.GueltigBis.Value.Date);
            var t = Basis(z, hit, null);
            bool offen = hit.VersichertAb != null
                && hit.VersichertAb <= DateOnly.FromDateTime(heute)
                && (hit.VersichertBis == null || hit.VersichertBis >= DateOnly.FromDateTime(heute));

            if (offen && listBis != null && hit.VersichertBis == null && hit.VersichertAb <= listBis)
            {
                t.Aktion = Beenden;
                t.Hinweis = $"Liste endet am {Fmt(listBis)}. In OneCrew ist die Versicherung noch offen.";
                res.Beenden.Add(t);
            }
            else if (!offen && hit.VersichertAb == null)
            {
                t.Aktion = Anlegen;
                t.Hinweis = listBis == null
                    ? "Auf der Liste versichert, in OneCrew noch nicht festgelegt."
                    : $"Auf der Liste versichert bis {Fmt(listBis)}, in OneCrew noch nicht festgelegt.";
                res.Festlegen.Add(t);
            }
            else if (offen && listBis == null && hit.VersichertBis == null && hit.VersichertAb == listAb)
            {
                t.Hinweis = "Versichert, Datum stimmt.";
                res.Passt.Add(t);
            }
            else if (offen && listBis == null && hit.VersichertBis == null && hit.VersichertAb > listAb)
            {
                // Beginn bei GastroSocial, oft schon im vorherigen Restaurant. OneCrew hat nur den späteren Erfassungsstand.
                t.Aktion = Datum;
                t.Hinweis = $"Liste ab {Fmt(listAb)} ist der Beginn bei GastroSocial. OneCrew steht auf {Fmt(hit.VersichertAb)}.";
                res.Abweichend.Add(t);
            }
            else if (offen && listBis == null && hit.VersichertBis == null && hit.VersichertAb < listAb)
            {
                t.Hinweis = $"Versichert. OneCrew ab {Fmt(hit.VersichertAb)}, Liste ab {Fmt(listAb)}.";
                res.Passt.Add(t);
            }
            else
            {
                t.Hinweis = offen
                    ? $"OneCrew ab {Fmt(hit.VersichertAb)}{(hit.VersichertBis == null ? "" : " bis " + Fmt(hit.VersichertBis))}, Liste ab {Fmt(listAb)}{(listBis == null ? "" : " bis " + Fmt(listBis))}."
                    : $"In OneCrew versichert ab {Fmt(hit.VersichertAb)}{(hit.VersichertBis == null ? "" : " bis " + Fmt(hit.VersichertBis))}, deckt heute nicht. Liste ab {Fmt(listAb)}.";
                res.Abweichend.Add(t);
            }
        }

        var resIds = new HashSet<int>(gesehen);
        foreach (var m in alle.Where(m => m.AktivInFiliale && !m.OhneLohn && m.VersichertAb != null
                     && m.VersichertAb <= DateOnly.FromDateTime(heute)
                     && (m.VersichertBis == null || m.VersichertBis >= DateOnly.FromDateTime(heute))
                     && !resIds.Contains(m.Id)))
        {
            res.NurInOneCrew.Add(new Treffer
            {
                EmployeeId = m.Id,
                Vorname = m.Vorname,
                Name = Person(m.Vorname, m.Nachname),
                OneCrewName = Person(m.Vorname, m.Nachname),
                Ahv = GastroSocialAbgleich.FormatAhv(m.Ahv),
                Personalnummer = m.Nummer,
                OneCrewAb = m.VersichertAb?.ToDateTime(TimeOnly.MinValue),
                OneCrewBis = m.VersichertBis?.ToDateTime(TimeOnly.MinValue),
                Hinweis = "In OneCrew versichert, fehlt auf dieser Liste. Bleibt unverändert.",
            });
        }

        foreach (var liste in new[] { res.Passt, res.Festlegen, res.Beenden, res.Abweichend, res.NichtZuordenbar, res.AndereFiliale, res.NurInOneCrew })
            liste.Sort((a, b) => string.Compare(a.Vorname, b.Vorname, StringComparison.OrdinalIgnoreCase) is var c && c != 0
                ? c : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return res;
    }

    private static Treffer Basis(Zeile z, Ma? hit, string? hinweis) => new()
    {
        EmployeeId = hit?.Id,
        Vorname = hit?.Vorname ?? z.Vorname,
        Name = Person(z.Vorname, z.Nachname),
        OneCrewName = hit == null ? null : Person(hit.Vorname, hit.Nachname),
        Ahv = GastroSocialAbgleich.FormatAhv(z.Ahv ?? hit?.Ahv),
        Personalnummer = hit?.Nummer,
        Bezeichnung = z.Bezeichnung,
        Geburtsdatum = z.Geburtsdatum ?? hit?.Geburtsdatum,
        GueltigAb = z.GueltigAb?.Date,
        GueltigBis = z.GueltigBis?.Date,
        OneCrewAb = hit?.VersichertAb?.ToDateTime(TimeOnly.MinValue),
        OneCrewBis = hit?.VersichertBis?.ToDateTime(TimeOnly.MinValue),
        Hinweis = hinweis,
    };

    private static Ma? Finde(Zeile z, List<Ma> alle)
    {
        var ahv = GastroSocialAbgleich.NormAhv(z.Ahv);
        if (ahv.Length == 13)
        {
            var hits = alle.Where(m => GastroSocialAbgleich.NormAhv(m.Ahv) == ahv).ToList();
            if (hits.Count == 1) return hits[0];
            if (hits.Count > 1) return hits.FirstOrDefault(h => h.InFiliale) ?? hits[0];
        }
        var key = GastroSocialAbgleich.NormName(z.Vorname) + "|" + GastroSocialAbgleich.NormName(z.Nachname);
        if (z.Geburtsdatum != null)
        {
            var hits = alle.Where(m => Schluessel(m) == key && m.Geburtsdatum?.Date == z.Geburtsdatum.Value.Date).ToList();
            if (hits.Count == 1) return hits[0];
        }
        var namen = alle.Where(m => Schluessel(m) == key).ToList();
        return namen.Count == 1 ? namen[0] : null;
    }

    private static string Schluessel(Ma m) =>
        GastroSocialAbgleich.NormName(m.Vorname) + "|" + GastroSocialAbgleich.NormName(m.Nachname);

    private static string Person(string? vor, string? nach) => $"{vor} {nach}".Trim();
    private static string Fmt(DateOnly? d) => d?.ToString("dd.MM.yyyy") ?? "—";

    public static List<Zeile> LiesExcel(Stream stream, string fileName)
    {
        IWorkbook wb = fileName.EndsWith(".xls", StringComparison.OrdinalIgnoreCase)
            ? new HSSFWorkbook(stream)
            : new XSSFWorkbook(stream);
        var sheet = wb.GetSheetAt(0) ?? throw new InvalidOperationException("Leeres Arbeitsblatt.");

        int kopf = -1;
        var spalten = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int r = 0; r <= Math.Min(sheet.LastRowNum, 15) && kopf < 0; r++)
        {
            var row = sheet.GetRow(r);
            if (row == null) continue;
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int c = 0; c < row.LastCellNum; c++)
            {
                var h = Ws(GastroSocialAbgleich.Text(row.GetCell(c)));
                if (h.Length > 0 && !map.ContainsKey(h)) map[h] = c;
            }
            bool gueltigAb = map.Keys.Any(k => k.Equals("Gültig ab", StringComparison.OrdinalIgnoreCase)
                || k.Equals("Gueltig ab", StringComparison.OrdinalIgnoreCase));
            if (map.ContainsKey("Name") && map.ContainsKey("Vorname")
                && map.Keys.Any(k => k.StartsWith("AHV", StringComparison.OrdinalIgnoreCase))
                && gueltigAb)
            {
                kopf = r;
                spalten = map;
            }
        }
        if (kopf < 0)
            throw new InvalidOperationException(
                "Kopfzeile mit «Name», «Vorname», «AHV-Nummer» und «Gültig ab» nicht gefunden. Erwartet ist die Versichertenliste der beruflichen Vorsorge.");

        int Sp(params string[] namen)
        {
            foreach (var n in namen)
                if (spalten.TryGetValue(n, out var i)) return i;
            return -1;
        }
        int cName = Sp("Name", "Nachname"), cVor = Sp("Vorname");
        int cAhv = Sp("AHV-Nummer", "AHV-Nr.", "AHV-Nr", "AHV");
        int cGeb = Sp("Geburtsdatum", "Geb.-Datum");
        int cBez = Sp("Bezeichnung", "Plan");
        int cAb = Sp("Gültig ab", "Gueltig ab");
        int cBis = Sp("Gültig bis", "Gueltig bis");

        var liste = new List<Zeile>();
        for (int r = kopf + 1; r <= sheet.LastRowNum; r++)
        {
            var row = sheet.GetRow(r);
            if (row == null) continue;
            string Feld(int c) => c >= 0 ? Ws(GastroSocialAbgleich.Text(row.GetCell(c))) : "";
            var name = Feld(cName);
            var vor = Feld(cVor);
            var ahv = Feld(cAhv);
            if (name.Length == 0 && vor.Length == 0 && ahv.Length == 0) continue;
            liste.Add(new Zeile
            {
                Nachname = Leer(name),
                Vorname = Leer(vor),
                Ahv = Leer(ahv),
                Geburtsdatum = cGeb >= 0 ? GastroSocialAbgleich.Datum(row.GetCell(cGeb)) : null,
                Bezeichnung = Leer(Feld(cBez)),
                GueltigAb = cAb >= 0 ? GastroSocialAbgleich.Datum(row.GetCell(cAb)) : null,
                GueltigBis = cBis >= 0 ? GastroSocialAbgleich.Datum(row.GetCell(cBis)) : null,
            });
        }
        return liste;
    }

    private static string Ws(string? s) => Regex.Replace((s ?? "").Trim(), @"\s+", " ");
    private static string? Leer(string s) => s.Length == 0 ? null : s;
}
