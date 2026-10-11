using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace HrSystem.Services;

/// <summary>
/// HR-Hub → Kontrolle → «AHV-Abgleich GastroSocial» (Walter 11.10.2026).
///
/// GastroSocial-Liste «Mitarbeitende» (Excel aus dem Portal: G · Name · Vorname ·
/// AHV-Nummer · Personalnummer · Geburtsdatum · Leistungen · Status · Statusänderung)
/// gegen die OneCrew-MA der Sidebar-Filiale (jede Filiale = eigenes Abrechnungskonto):
///   • Abmelden — bei GastroSocial «Beschäftigt», in OneCrew in dieser Filiale nicht mehr aktiv
///   • Nicht gemeldet — in OneCrew aktiv (mit Lohn), in der Liste fehlt sie/er
///   • Nicht zuordenbar — Zeile ohne OneCrew-MA
///   • Gemeldet — passt (ggf. mit Hinweis, z.B. AHV-Nummer weicht ab)
/// Match: AHV-Nummer (über alle Filialen) → Name + Geburtsdatum → Name (eindeutig in der Filiale).
/// Reine Auswertung, schreibt nichts.
/// </summary>
public static class GastroSocialAbgleich
{
    public class Zeile
    {
        public int Nr { get; set; }
        public string? Geschlecht { get; set; }
        public string? Nachname { get; set; }
        public string? Vorname { get; set; }
        public string? Ahv { get; set; }
        public string? Personalnummer { get; set; }
        public DateTime? Geburtsdatum { get; set; }
        public string? Leistungen { get; set; }
        public string? Status { get; set; }
        public DateTime? Statusaenderung { get; set; }
        public bool Beschaeftigt => (Status ?? "").Trim().StartsWith("besch", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>OneCrew-MA, aufbereitet für den Abgleich (alle Filialen — für den AHV-Match).</summary>
    public class Ma
    {
        public int Id { get; set; }
        public string? Nummer { get; set; }
        public string? Vorname { get; set; }
        public string? Nachname { get; set; }
        public string? Ahv { get; set; }
        public DateTime? Geburtsdatum { get; set; }
        public DateTime? Eintritt { get; set; }
        /// <summary>Hat (oder hatte) einen Vertrag in der Filiale.</summary>
        public bool InFiliale { get; set; }
        /// <summary>Heute aktiv in der Filiale (laufender Vertrag, kein vergangener Austritt).</summary>
        public bool AktivInFiliale { get; set; }
        public bool OhneLohn { get; set; }
        /// <summary>Austritt bzw. Ende des letzten Vertrags in dieser Filiale.</summary>
        public DateTime? AustrittFiliale { get; set; }
        /// <summary>Austrittsdatum in der Zukunft (Kündigung läuft).</summary>
        public DateTime? AustrittGeplant { get; set; }
        /// <summary>Andere Filiale mit laufendem Vertrag (Übertritt).</summary>
        public string? AktivInAndererFiliale { get; set; }
    }

    public class Treffer
    {
        public int? Zeile { get; set; }
        public string? Name { get; set; }
        public string? Ahv { get; set; }
        public DateTime? Geburtsdatum { get; set; }
        public string? Leistungen { get; set; }
        public string? Status { get; set; }
        public DateTime? Statusaenderung { get; set; }
        public int? EmployeeId { get; set; }
        public string? OneCrewName { get; set; }
        public string? Personalnummer { get; set; }
        public string? OneCrewAhv { get; set; }
        public DateTime? Eintritt { get; set; }
        public DateTime? Austritt { get; set; }
        public string? MatchArt { get; set; } // AHV | NAME_GEB | NAME
        public string? Hinweis { get; set; }
    }

    public class Ergebnis
    {
        public int Zeilen { get; set; }
        public int GemeldetBeschaeftigt { get; set; }
        public int OneCrewAktiv { get; set; }
        public int OhneLohn { get; set; }
        public List<Treffer> Abmelden { get; set; } = new();
        public List<Treffer> NichtGemeldet { get; set; } = new();
        public List<Treffer> NichtZuordenbar { get; set; } = new();
        public List<Treffer> Gemeldet { get; set; } = new();
    }

    public static Ergebnis Vergleiche(IReadOnlyList<Zeile> zeilen, IReadOnlyList<Ma> alle, DateTime heute)
    {
        var res = new Ergebnis
        {
            Zeilen = zeilen.Count,
            GemeldetBeschaeftigt = zeilen.Count(z => z.Beschaeftigt),
            OneCrewAktiv = alle.Count(m => m.AktivInFiliale && !m.OhneLohn),
            OhneLohn = alle.Count(m => m.AktivInFiliale && m.OhneLohn),
        };

        var byAhv = alle.Where(m => NormAhv(m.Ahv).Length == 13).ToLookup(m => NormAhv(m.Ahv));
        var filiale = alle.Where(m => m.InFiliale).ToList();
        var byNameGeb = filiale.Where(m => m.Geburtsdatum != null)
            .ToLookup(m => NameKey(m.Vorname, m.Nachname) + "|" + m.Geburtsdatum!.Value.ToString("yyyyMMdd"));
        var byName = filiale.ToLookup(m => NameKey(m.Vorname, m.Nachname));

        var gemeldeteIds = new HashSet<int>();
        foreach (var z in zeilen)
        {
            Ma? hit = null;
            string? art = null;
            var ahv = NormAhv(z.Ahv);
            if (ahv.Length == 13 && byAhv.Contains(ahv))
            {
                var c = byAhv[ahv].ToList();
                hit = c.FirstOrDefault(m => m.InFiliale) ?? c.First();
                art = "AHV";
            }
            if (hit == null && z.Geburtsdatum != null)
            {
                var geb = z.Geburtsdatum.Value.ToString("yyyyMMdd");
                var c = byNameGeb[NameKey(z.Vorname, z.Nachname) + "|" + geb].ToList();
                if (c.Count == 0) c = byNameGeb[NameKey(z.Nachname, z.Vorname) + "|" + geb].ToList();
                if (c.Count == 1) { hit = c[0]; art = "NAME_GEB"; }
            }
            if (hit == null)
            {
                var c = byName[NameKey(z.Vorname, z.Nachname)].ToList();
                if (c.Count == 1) { hit = c[0]; art = "NAME"; }
            }

            var t = new Treffer
            {
                Zeile = z.Nr,
                Name = $"{z.Vorname} {z.Nachname}".Trim(),
                Ahv = FormatAhv(z.Ahv),
                Geburtsdatum = z.Geburtsdatum,
                Leistungen = z.Leistungen,
                Status = z.Status,
                Statusaenderung = z.Statusaenderung,
            };
            if (hit == null)
            {
                if (z.Beschaeftigt)
                {
                    t.Hinweis = "In OneCrew nicht gefunden — wenn die Person nicht (mehr) bei uns arbeitet: bei GastroSocial abmelden.";
                    res.NichtZuordenbar.Add(t);
                }
                continue;
            }

            t.EmployeeId = hit.Id;
            t.OneCrewName = $"{hit.Vorname} {hit.Nachname}".Trim();
            t.Personalnummer = hit.Nummer;
            t.OneCrewAhv = FormatAhv(hit.Ahv);
            t.Eintritt = hit.Eintritt;
            t.Austritt = hit.AustrittFiliale;
            t.MatchArt = art;
            var hinweise = new List<string>();
            if (art != "AHV")
                hinweise.Add(string.IsNullOrWhiteSpace(hit.Ahv)
                    ? "AHV-Nummer fehlt in OneCrew"
                    : "AHV-Nummer weicht ab (OneCrew " + FormatAhv(hit.Ahv) + ")");

            if (!z.Beschaeftigt)
            {
                // Bei GastroSocial nicht (mehr) beschäftigt, in OneCrew aktiv → wieder anmelden.
                if (hit.AktivInFiliale && !hit.OhneLohn)
                {
                    hinweise.Insert(0, $"Bei GastroSocial «{z.Status}», in OneCrew aktiv — neu anmelden");
                    t.Hinweis = string.Join(" · ", hinweise);
                    res.NichtGemeldet.Add(t);
                    gemeldeteIds.Add(hit.Id);
                }
                continue;
            }

            gemeldeteIds.Add(hit.Id);
            if (!hit.AktivInFiliale)
            {
                var grund = !hit.InFiliale
                    ? (hit.AktivInAndererFiliale != null ? $"arbeitet in {hit.AktivInAndererFiliale}, nie in dieser Filiale" : "nie in dieser Filiale angestellt")
                    : hit.AustrittFiliale != null ? $"ausgetreten per {hit.AustrittFiliale:dd.MM.yyyy}" : "kein laufender Vertrag in dieser Filiale";
                if (hit.InFiliale && hit.AktivInAndererFiliale != null) grund += $" (Übertritt → {hit.AktivInAndererFiliale})";
                hinweise.Insert(0, grund);
                t.Hinweis = string.Join(" · ", hinweise);
                res.Abmelden.Add(t);
                continue;
            }
            if (hit.OhneLohn) hinweise.Add("in OneCrew «MA ohne Lohn»");
            if (hit.AustrittGeplant != null) hinweise.Add($"Austritt am {hit.AustrittGeplant:dd.MM.yyyy} — danach abmelden");
            t.Hinweis = hinweise.Count > 0 ? string.Join(" · ", hinweise) : null;
            res.Gemeldet.Add(t);
        }

        foreach (var m in filiale.Where(m => m.AktivInFiliale && !m.OhneLohn && !gemeldeteIds.Contains(m.Id))
                     .OrderBy(m => m.Vorname ?? "", StringComparer.OrdinalIgnoreCase)
                     .ThenBy(m => m.Nachname ?? "", StringComparer.OrdinalIgnoreCase))
        {
            var h = new List<string>();
            if (m.Eintritt != null && m.Eintritt.Value.Date > heute.Date) h.Add($"Eintritt erst am {m.Eintritt:dd.MM.yyyy}");
            if (string.IsNullOrWhiteSpace(m.Ahv)) h.Add("AHV-Nummer fehlt in OneCrew");
            if (m.AustrittGeplant != null) h.Add($"Austritt am {m.AustrittGeplant:dd.MM.yyyy}");
            res.NichtGemeldet.Add(new Treffer
            {
                Name = $"{m.Vorname} {m.Nachname}".Trim(),
                EmployeeId = m.Id,
                OneCrewName = $"{m.Vorname} {m.Nachname}".Trim(),
                Personalnummer = m.Nummer,
                OneCrewAhv = FormatAhv(m.Ahv),
                Geburtsdatum = m.Geburtsdatum,
                Eintritt = m.Eintritt,
                Hinweis = h.Count > 0 ? string.Join(" · ", h) : null,
            });
        }

        res.Abmelden = res.Abmelden.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
        res.NichtZuordenbar = res.NichtZuordenbar.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
        res.Gemeldet = res.Gemeldet.OrderBy(t => t.OneCrewName, StringComparer.OrdinalIgnoreCase).ToList();
        return res;
    }

    // ─────────────────────────── Excel lesen ───────────────────────────

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
                var h = Ws(Text(row.GetCell(c)));
                if (h.Length > 0 && !map.ContainsKey(h)) map[h] = c;
            }
            if (map.ContainsKey("Name") && map.ContainsKey("Vorname") && map.Keys.Any(k => k.StartsWith("AHV", StringComparison.OrdinalIgnoreCase)))
            {
                kopf = r;
                spalten = map;
            }
        }
        if (kopf < 0)
            throw new InvalidOperationException("Kopfzeile mit «Name», «Vorname» und «AHV-Nummer» nicht gefunden.");

        int Sp(params string[] namen)
        {
            foreach (var n in namen) if (spalten.TryGetValue(n, out var i)) return i;
            return -1;
        }
        int cG = Sp("G", "Geschlecht"), cName = Sp("Name", "Nachname"), cVor = Sp("Vorname");
        int cAhv = Sp("AHV-Nummer", "AHV-Nr.", "AHV-Nr", "AHV", "Sozialversicherungsnummer");
        int cNr = Sp("Personalnummer", "Personal-Nr.", "Pers. Nr.");
        int cGeb = Sp("Geburtsdatum", "Geb.-Datum");
        int cLst = Sp("Leistungen");
        int cStatus = Sp("Status");
        int cAend = Sp("Statusänderung", "Statusaenderung");

        var liste = new List<Zeile>();
        for (int r = kopf + 1; r <= sheet.LastRowNum; r++)
        {
            var row = sheet.GetRow(r);
            if (row == null) continue;
            string Feld(int c) => c >= 0 ? Ws(Text(row.GetCell(c))) : "";
            var name = Feld(cName);
            var vor = Feld(cVor);
            var ahv = Feld(cAhv);
            if (name.Length == 0 && vor.Length == 0 && ahv.Length == 0) continue;
            liste.Add(new Zeile
            {
                Nr = r + 1,
                Geschlecht = Leer(Feld(cG)),
                Nachname = Leer(name),
                Vorname = Leer(vor),
                Ahv = Leer(ahv),
                Personalnummer = Leer(Feld(cNr)),
                Geburtsdatum = cGeb >= 0 ? Datum(row.GetCell(cGeb)) : null,
                Leistungen = Leer(Feld(cLst)),
                Status = Leer(Feld(cStatus)),
                Statusaenderung = cAend >= 0 ? Datum(row.GetCell(cAend)) : null,
            });
        }
        return liste;
    }

    private static string Text(ICell? cell)
    {
        if (cell == null) return "";
        return cell.CellType switch
        {
            CellType.Numeric => DateUtil.IsCellDateFormatted(cell) && cell.DateCellValue != null
                ? cell.DateCellValue.Value.ToString("dd.MM.yyyy")
                : cell.NumericCellValue.ToString("0.##########", CultureInfo.InvariantCulture),
            CellType.Boolean => cell.BooleanCellValue ? "true" : "false",
            _ => cell.ToString() ?? ""
        };
    }

    private static DateTime? Datum(ICell? cell)
    {
        if (cell == null) return null;
        try
        {
            if (cell.CellType == CellType.Numeric)
            {
                if (DateUtil.IsCellDateFormatted(cell) && cell.DateCellValue != null) return cell.DateCellValue.Value.Date;
                var v = cell.NumericCellValue;
                if (v > 1000 && v < 80000) return DateUtil.GetJavaDate(v).Date;
            }
        }
        catch { /* als Text versuchen */ }
        var s = (cell.ToString() ?? "").Trim();
        var formate = new[] { "dd.MM.yyyy", "d.M.yyyy", "dd.MM.yy", "yyyy-MM-dd", "dd/MM/yyyy" };
        return DateTime.TryParseExact(s, formate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.Date : null;
    }

    // ─────────────────────────── Normalisierung ───────────────────────────

    public static string NormAhv(string? s) => new string((s ?? "").Where(char.IsDigit).ToArray());

    public static string? FormatAhv(string? s)
    {
        var d = NormAhv(s);
        if (d.Length != 13) return string.IsNullOrWhiteSpace(s) ? null : s!.Trim();
        return $"{d[..3]}.{d.Substring(3, 4)}.{d.Substring(7, 4)}.{d.Substring(11, 2)}";
    }

    /// <summary>Kleinbuchstaben, ohne Akzente/Apostroph/Bindestrich — «L'udmila» = «Ľudmila».</summary>
    public static string NormName(string? s)
    {
        var t = (s ?? "").ToLowerInvariant().Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("ß", "ss");
        var sb = new StringBuilder();
        foreach (var ch in t.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (ch is '\'' or '’' or '`' or '´') continue;
            sb.Append(char.IsLetter(ch) ? ch : ' ');
        }
        return Ws(sb.ToString());
    }

    private static string NameKey(string? vorname, string? nachname) => NormName(vorname) + "|" + NormName(nachname);
    private static string Ws(string? s) => Regex.Replace((s ?? "").Trim(), @"\s+", " ");
    private static string? Leer(string s) => s.Length == 0 ? null : s;
}
