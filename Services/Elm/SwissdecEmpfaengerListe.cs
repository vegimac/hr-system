using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace HrSystem.Services.Elm;

/// <summary>
/// Liest die Swissdec «Empfängerliste» (XLSX von swissdec.ch/data-receiver)
/// und mappt sie auf OneCrew-Katalogzeilen (Walter 17.10.2026).
/// Eine Excel-Zeile kann mehrere Domänen ergeben (AHV+FAK, UVG+KTG, …).
/// </summary>
public static class SwissdecEmpfaengerListe
{
    public const string DownloadUrl =
        "https://www.swissdec.ch/document/share/325/6b26f12a-60ff-444b-9877-c492e66accfa";
    public const string InfoUrl = "https://www.swissdec.ch/data-receiver";

    public record Zeile(
        string Art,
        string Bezeichnung,
        string? Kassennummer,
        string? UidNummer,
        string? KantonCode,
        string? SupportEmail,
        string Quelle);

    public record Diff(
        string Status,          // neu | aenderung | gleich
        string Art,
        string Bezeichnung,
        string? Kassennummer,
        string? UidNummer,
        string? KantonCode,
        string? SupportEmail,
        int? BestehendId,
        string? AenderungText);

    /// <summary>Parst das Swissdec-XLSX (Sheet «Endempfänger» oder erstes Datenblatt).</summary>
    public static List<Zeile> Parse(Stream xlsx)
    {
        IWorkbook wb = new XSSFWorkbook(xlsx);
        ISheet? sheet = null;
        for (int i = 0; i < wb.NumberOfSheets; i++)
        {
            var s = wb.GetSheetAt(i);
            if ((s.SheetName ?? "").Contains("Endempf", StringComparison.OrdinalIgnoreCase)
             || (s.SheetName ?? "").Contains("empf", StringComparison.OrdinalIgnoreCase))
            { sheet = s; break; }
        }
        sheet ??= wb.GetSheetAt(Math.Min(1, wb.NumberOfSheets - 1));

        var result = new List<Zeile>();
        string section = ""; // versicherer | ahv | statistik | steuern
        int lastRow = sheet.LastRowNum;

        for (int r = 0; r <= lastRow; r++)
        {
            var row = sheet.GetRow(r);
            if (row == null) continue;
            var cells = Enumerable.Range(0, Math.Max(11, (int)row.LastCellNum))
                .Select(c => CellText(row.GetCell(c))).ToList();
            var a0 = cells.ElementAtOrDefault(0) ?? "";
            var joined = string.Join(" ", cells.Where(c => !string.IsNullOrWhiteSpace(c)));

            if (IsHeaderLike(joined))
            {
                // Reihenfolge wichtig: «No assureur» / «N° assciuratore» steht in JEDEM
                // Spaltenkopf — darf den Abschnitt nicht auf Versicherer zurücksetzen.
                if (ContainsAny(joined, "Lohnausweis", "Quellen", "Steuern", "Impôts", "Imposte"))
                    section = "steuern";
                else if (ContainsAny(joined, "Statistik", "Statistique", "BFS", "OFS")
                         && !ContainsAny(joined, "AHV", "AVS", "FAK"))
                    section = "statistik";
                else if (ContainsAny(joined, "AHV", "AVS", "FAK", "CAF")
                         && ContainsAny(joined, "AK-Nr", "no CC", "Datenempfänger", "Destinataire"))
                    section = "ahv";
                else if (ContainsAny(joined, "UVG", "UVGZ", "KTG")
                         && ContainsAny(joined, "Vers.-Nr", "No assureur", "Datenempfänger"))
                    section = "versicherer";
                // sonst Abschnitt unverändert lassen (Titelzeile hat ihn schon gesetzt)
                continue;
            }

            // Abschnitts-Titel (eigene Zeile, oft ohne Header-Wörter)
            if (string.IsNullOrWhiteSpace(a0) == false
                && LooksLikeAbschnittstitel(a0))
            {
                if (ContainsAny(a0, "Versicherer", "Assureur")) section = "versicherer";
                else if (ContainsAny(a0, "Statistik", "Statistique")) section = "statistik";
                else if (ContainsAny(a0, "Steuern", "Impôts", "Imposte")) section = "steuern";
                else if (ContainsAny(a0, "AHV", "AVS", "FAK", "CAF")) section = "ahv";
                continue;
            }

            if (string.IsNullOrWhiteSpace(section)) continue;
            if (!LooksLikeNummer(a0)) continue;
            var name = cells.ElementAtOrDefault(1)?.Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (name.StartsWith("Erklärung", StringComparison.OrdinalIgnoreCase)
             || name.StartsWith("Déclaration", StringComparison.OrdinalIgnoreCase))
                continue;

            var nr = NormNr(a0);
            var mail = PickMail(cells);
            var uid = PickUid(cells);

            switch (section)
            {
                case "versicherer":
                    // Spalten: Nr, Name, UID, UVG, UVGZ, KTG, BVG, ELM, KLE, SUA, Mail
                    AddIfFlag(result, cells, 3, "UVG", name!, nr, uid, null, mail, "Versicherer");
                    AddIfFlag(result, cells, 4, "UVGZ", name!, nr, uid, null, mail, "Versicherer");
                    AddIfFlag(result, cells, 5, "KTG", name!, nr, uid, null, mail, "Versicherer");
                    // BVG nur bei gesetztem Flag (oder «List» = siehe Pensionskassen — dann auch BVG)
                    if (IsFlag(cells.ElementAtOrDefault(6))
                        || (cells.ElementAtOrDefault(6) ?? "").Contains("List", StringComparison.OrdinalIgnoreCase))
                        result.Add(new Zeile("BVG", name!, nr, uid, null, mail, "Versicherer"));
                    break;

                case "ahv":
                    // Spalten: AK-Nr, Name, …, AHV, FAK, Jahr, ELM, …, Mail
                    // Flags meist Index 4/5 — fallweise suchen
                    var ahv = FlagInRow(cells, "AHV", "AVS") || IsFlag(cells.ElementAtOrDefault(4));
                    var fak = FlagInRow(cells, "FAK", "CAF") || IsFlag(cells.ElementAtOrDefault(5));
                    // Wenn weder Spalte erkennbar noch Flag: beide (wie die meisten AK)
                    if (!ahv && !fak) { ahv = true; fak = true; }
                    if (ahv) result.Add(new Zeile("AUSGLEICHSKASSE", name!, nr, null, null, mail, "AHV/FAK"));
                    if (fak) result.Add(new Zeile("FAK", name!, nr, null, null, mail, "AHV/FAK"));
                    break;

                case "statistik":
                    result.Add(new Zeile("ANDERE", name!, nr, uid, null, mail, "Statistik"));
                    break;

                case "steuern":
                    var kanton = nr.Length == 2 ? nr.ToUpperInvariant() : null;
                    // Spalten: Kanton, Name, …, Lohnausweis, Ausland, Grenzgänger, QST, ELM, Mail
                    var lohnausweis = IsFlag(cells.ElementAtOrDefault(3));
                    var qst = IsFlag(cells.ElementAtOrDefault(6)) || FlagInRow(cells, "Quellen");
                    if (!lohnausweis && !qst) { qst = true; lohnausweis = true; } // konservativ beide
                    if (qst)
                        result.Add(new Zeile("QST", name!, nr, null, kanton, mail, "Steuern"));
                    if (lohnausweis)
                        result.Add(new Zeile("LOHNAUSWEIS", name!, nr, null, kanton, mail, "Steuern"));
                    break;
            }
        }

        // Deduplizieren (Art + Nr)
        return result
            .GroupBy(z => Key(z.Art, z.Kassennummer))
            .Select(g => g.First())
            .OrderBy(z => z.Art).ThenBy(z => z.Bezeichnung, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static List<Diff> Vergleichen(IEnumerable<Zeile> liste, IEnumerable<Models.LohndatenEmpfaenger> bestand)
    {
        var map = bestand.ToDictionary(e => Key(e.Art, e.Kassennummer ?? e.KantonCode), e => e, StringComparer.OrdinalIgnoreCase);
        var diffs = new List<Diff>();
        var gesehen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var z in liste)
        {
            var k = Key(z.Art, z.Kassennummer);
            gesehen.Add(k);
            if (!map.TryGetValue(k, out var alt))
            {
                diffs.Add(new Diff("neu", z.Art, z.Bezeichnung, z.Kassennummer, z.UidNummer, z.KantonCode, z.SupportEmail, null, null));
                continue;
            }
            var aenderungen = new List<string>();
            if (!Eq(alt.Bezeichnung, z.Bezeichnung)) aenderungen.Add($"Name: «{alt.Bezeichnung}» → «{z.Bezeichnung}»");
            if (!Eq(alt.UidNummer, z.UidNummer) && !string.IsNullOrWhiteSpace(z.UidNummer))
                aenderungen.Add($"UID: «{alt.UidNummer}» → «{z.UidNummer}»");
            if (!Eq(alt.SupportEmail, z.SupportEmail) && !string.IsNullOrWhiteSpace(z.SupportEmail))
                aenderungen.Add($"E-Mail: «{alt.SupportEmail}» → «{z.SupportEmail}»");
            if (!Eq(alt.KantonCode, z.KantonCode) && !string.IsNullOrWhiteSpace(z.KantonCode))
                aenderungen.Add($"Kanton: «{alt.KantonCode}» → «{z.KantonCode}»");
            if (aenderungen.Count == 0)
                diffs.Add(new Diff("gleich", z.Art, z.Bezeichnung, z.Kassennummer, z.UidNummer, z.KantonCode, z.SupportEmail, alt.Id, null));
            else
                diffs.Add(new Diff("aenderung", z.Art, z.Bezeichnung, z.Kassennummer, z.UidNummer, z.KantonCode, z.SupportEmail, alt.Id, string.Join("; ", aenderungen)));
        }

        return diffs;
    }

    public static string Key(string art, string? nr)
        => $"{(art ?? "").Trim().ToUpperInvariant()}|{NormNr(nr)}";

    public static string NormNr(string? nr)
    {
        if (string.IsNullOrWhiteSpace(nr)) return "";
        var s = nr.Trim().Replace('\u00a0', ' ').Replace("​", ""); // zero-width
        s = Regex.Replace(s, @"\s+", "");
        return s.ToUpperInvariant();
    }

    static void AddIfFlag(List<Zeile> list, List<string?> cells, int col, string art,
        string name, string nr, string? uid, string? kanton, string? mail, string quelle)
    {
        if (!IsFlag(cells.ElementAtOrDefault(col))) return;
        list.Add(new Zeile(art, name, nr, uid, kanton, mail, quelle));
    }

    static bool FlagInRow(List<string?> cells, params string[] markers)
    {
        // nicht nötig für aktuelle Datei — Reserved
        return false;
    }

    static bool IsFlag(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return false;
        var s = v.Trim().ToLowerInvariant().Replace("​", "");
        return s is "x" or "✓" or "✔" or "ja" or "yes"
            || s.Contains('x') && s.Length <= 3;
    }

    static bool LooksLikeAbschnittstitel(string a0)
    {
        var t = (a0 ?? "").Trim();
        if (t.Length == 0 || t.Length > 80) return false;
        // «Steuern / Impôts / Imposte», «Statistik / …», «(AHV, AVS) (FAK, CAF)»
        return ContainsAny(t, "Steuern", "Impôts", "Imposte", "Statistik", "Statistique",
            "Versicherer", "Assureur")
            || (ContainsAny(t, "AHV", "AVS", "FAK") && !LooksLikeNummer(t));
    }

    static bool LooksLikeNummer(string a0)
    {
        var s = NormNr(a0);
        if (s.Length == 0) return false;
        // AK 046.000, Vers S14 / L592 / K329, Kanton AG, BFS «SwissFederal…»
        if (Regex.IsMatch(s, @"^[A-Z]{2}$")) return true;                 // Kanton
        if (Regex.IsMatch(s, @"^\d{3}\.\d{3}$")) return true;             // AK
        if (Regex.IsMatch(s, @"^[A-Z]?\d{1,6}(\.\d+)?$")) return true;   // S14, L592
        if (s.Contains("SWISSFEDERAL", StringComparison.Ordinal)) return true;
        if (Regex.IsMatch(s, @"^[A-Z]\d+$")) return true;
        // Kein breiter Fallback — sonst gelten Titel wie «Steuern / Impôts» als Nummer
        // und der Abschnittswechsel greift nie (QST/Lohnausweis fehlen sonst).
        return false;
    }

    static bool IsHeaderLike(string joined)
    {
        var j = joined.ToLowerInvariant();
        return j.Contains("datenempfänger") || j.Contains("destinataire")
            || j.Contains("vers.-nr") || j.Contains("ak-nr")
            || j.Contains("support e-mail") || j.Contains("no assureur");
    }

    static bool ContainsAny(string hay, params string[] needles)
        => needles.Any(n => hay.Contains(n, StringComparison.OrdinalIgnoreCase));

    static string? PickUid(List<string?> cells)
    {
        foreach (var c in cells)
        {
            if (string.IsNullOrWhiteSpace(c)) continue;
            var u = c.Trim().ToUpperInvariant().Replace("​", "");
            // Nur gültige UID speichern — ungültige (CH-… / falsche Prüfziffer) weglassen
            if (ElmStammdatenPruefung.UidBfsGueltig(u)) return u;
        }
        return null;
    }

    static string? PickMail(List<string?> cells)
    {
        for (int i = cells.Count - 1; i >= 0; i--)
        {
            var c = cells[i];
            if (string.IsNullOrWhiteSpace(c)) continue;
            var s = c.Trim().Replace("​", "");
            if (s.Contains('@') && s.Length < 120) return s.Split(';')[0].Trim();
        }
        return null;
    }

    static string? CellText(ICell? cell)
    {
        if (cell == null) return null;
        try
        {
            var s = cell.CellType switch
            {
                CellType.String => cell.StringCellValue,
                CellType.Numeric => cell.NumericCellValue.ToString(CultureInfo.InvariantCulture),
                CellType.Boolean => cell.BooleanCellValue ? "x" : null,
                CellType.Formula => cell.ToString(),
                _ => cell.ToString(),
            };
            if (string.IsNullOrWhiteSpace(s)) return null;
            // unsichtbare Zeichen aus Swissdec-Export
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
            {
                if (ch is '\u200b' or '\u200c' or '\u200d' or '\ufeff' or '\u00a0') continue;
                sb.Append(ch);
            }
            return sb.ToString().Trim();
        }
        catch { return null; }
    }

    static bool Eq(string? a, string? b)
        => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
}
