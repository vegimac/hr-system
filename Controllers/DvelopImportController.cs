using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using System.Globalization;
using System.IO.Compression;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;

namespace HrSystem.Controllers;

/// <summary>
/// Importiert Dokumente aus einem d.velop-Export (Metadaten als CSV oder XLSX
/// + ZIP mit den eigentlichen Dokumenten).
/// Match-Logik:
///   - Mitarbeiter via Vorname + Nachname + Geburtsdatum (matcht "alt"-suffixed)
///   - Filiale via "Mandant"-Spalte (z.B. "58 McDonald's Restaurant Oftringen" → 058)
///   - Kategorie via "Kategorie"-Spalte (Prefix "HR: " entfernen)
///   - Typ via passende Sub-Spalte ("Dokumenttyp Absenzen" etc.)
///   - File via XG-ID aus "Dokument-ID" → Match in ZIP-Filenames
///
/// Format-Erkennung: CSV (Semikolon-getrennt, UTF-8) ODER XLSX (Sheet
/// "ExcelExport" oder erstes Sheet) — automatisch via Datei-Endung. Beide
/// liefern dieselben Spalten-Header, der Parser baut intern eine einheitliche
/// rows-Struktur, der Rest der Logik ist Format-agnostisch.
/// </summary>
[Authorize(Roles = "admin,superuser")]
[ApiController]
[Route("api/documents/import-dvelop")]
public class DvelopImportController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly string _storagePath;

    public DvelopImportController(AppDbContext db, IConfiguration config, IWebHostEnvironment env)
    {
        _db = db;
        var configured = config["Documents:StoragePath"];
        if (string.IsNullOrWhiteSpace(configured))
            configured = Path.Combine(env.ContentRootPath, "data", "documents");
        _storagePath = configured;
    }

    public class DvelopResult
    {
        public bool DryRun { get; set; }
        public int TotalRows { get; set; }
        public int Imported { get; set; }
        public int SkippedNoEmployee { get; set; }
        public int SkippedNoBranch { get; set; }
        public int SkippedNoCategory { get; set; }
        public int SkippedNoFile { get; set; }
        public int SkippedDuplicate { get; set; }
        public List<DvelopRow> Preview { get; set; } = new();
    }

    public class DvelopRow
    {
        public int RowNum { get; set; }
        public string XgId { get; set; } = "";
        public string Filename { get; set; } = "";
        public string EmployeeName { get; set; } = "";
        public DateOnly? DateOfBirth { get; set; }
        public int? EmployeeId { get; set; }
        public string? BranchCode { get; set; }
        public string? KategorieName { get; set; }
        public string? TypName { get; set; }
        public int? DokumentTypId { get; set; }
        public string? Bemerkung { get; set; }
        public DateOnly? GueltigVon { get; set; }
        public string Action { get; set; } = "";
        public string? Reason { get; set; }
    }

    [HttpPost]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = 2_000_000_000)]
    public async Task<IActionResult> Import(
        [FromForm] IFormFile csvFile,
        [FromForm] IFormFile zipFile,
        [FromForm] int employeeId = 0,
        [FromForm] bool dryRun = true,
        [FromForm] string? rowOverrides = null)
    {
        // Walter-Vorgabe 10.07.2026 (Massen-Modus): employeeId ist OPTIONAL.
        // Ohne Vorauswahl (0) wird der Ziel-MA PRO ZEILE aus den CSV-Spalten
        // aufgelöst (MA-Nummer inkl. Alias-Nummern «alt»-tolerant, sonst
        // Vorname+Nachname+Geburtsdatum) — damit lässt sich ein d.velop-Export
        // über VIELE Dossiers (z.B. ganze Filiale) in einem Lauf importieren.
        var selectedEmp = employeeId > 0 ? await _db.Employees.FindAsync(employeeId) : null;
        if (employeeId > 0 && selectedEmp == null) return BadRequest("Gewählter Mitarbeiter nicht gefunden.");
        if (csvFile == null || csvFile.Length == 0) return BadRequest("Metadaten-Datei (CSV oder XLSX) fehlt.");
        if (zipFile == null || zipFile.Length == 0) return BadRequest("ZIP fehlt.");

        // Per-Row MA-Overrides aus dem Frontend: Walter kann im Preview pro
        // Datei einen anderen MA wählen als den global ausgewählten. Format:
        // JSON-Map { "XG00010269": 1379, "XG00003684": 1387, … }.
        // Kein Override für eine Zeile → Fallback auf selectedEmp.
        Dictionary<string, int> overrides = new(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(rowOverrides))
        {
            try
            {
                var parsed = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, int>>(rowOverrides!);
                if (parsed != null) overrides = new Dictionary<string, int>(parsed, StringComparer.OrdinalIgnoreCase);
            }
            catch { /* ignorieren — defekte JSON heisst einfach „keine Overrides" */ }
        }
        // Alle übergebenen Override-MAs einmal vorab laden, damit wir pro Row
        // nicht einzeln in die DB müssen. Plus Cache für BranchCode-Lookup.
        var overrideEmps = overrides.Values.Distinct().ToList();
        var overrideEmpsById = overrideEmps.Count > 0
            ? await _db.Employees
                .Include(e => e.Employments).ThenInclude(em => em.CompanyProfile)
                .Where(e => overrideEmps.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id)
            : new Dictionary<int, Employee>();

        // Pro-Zeile-Auflösung: alle MA + Alias-Nummern einmal laden.
        var allEmps = await _db.Employees
            .Include(e => e.NumberAliases)
            .Where(e => !e.IsHidden)
            .ToListAsync();
        static string NormNum(string? n) =>
            System.Text.RegularExpressions.Regex.Replace((n ?? "").Trim(), "alt$", "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var empsByNum = new Dictionary<string, List<Employee>>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in allEmps)
        {
            void AddNum(string? n)
            {
                var k = NormNum(n);
                if (k.Length == 0) return;
                if (!empsByNum.TryGetValue(k, out var l)) empsByNum[k] = l = new List<Employee>();
                if (!l.Contains(e)) l.Add(e);
            }
            AddNum(e.EmployeeNumber);
            foreach (var a in e.NumberAliases) AddNum(a.Number);
        }
        Employee? ResolveRowEmployee(string? maNr, string? vn, string? nn, DateOnly? geb)
        {
            var k = NormNum(maNr);
            if (k.Length > 0 && empsByNum.TryGetValue(k, out var byN) && byN.Count == 1) return byN[0];
            if (!string.IsNullOrWhiteSpace(vn) && !string.IsNullOrWhiteSpace(nn))
            {
                var hits = allEmps.Where(e =>
                        string.Equals((e.FirstName ?? "").Trim(), vn.Trim(), StringComparison.OrdinalIgnoreCase)
                     && string.Equals((e.LastName ?? "").Trim(), nn.Trim(), StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (hits.Count == 1) return hits[0];
                if (hits.Count > 1 && geb.HasValue)
                {
                    var byDob = hits.Where(e => e.DateOfBirth.HasValue
                        && DateOnly.FromDateTime(e.DateOfBirth.Value) == geb.Value).ToList();
                    if (byDob.Count == 1) return byDob[0];
                }
            }
            return null;
        }

        var result = new DvelopResult { DryRun = dryRun };

        // Metadaten einlesen — CSV (Semikolon) oder XLSX. Endung entscheidet,
        // bei .xlsx fällt der Parser zurück auf NPOI-XSSFWorkbook, sonst CSV.
        List<string> headers;
        List<List<string>> dataRows;
        try
        {
            (headers, dataRows) = ReadMetadataFile(csvFile);
        }
        catch (Exception ex)
        {
            return BadRequest($"Metadaten-Datei konnte nicht gelesen werden: {ex.Message}");
        }
        if (headers.Count == 0 || dataRows.Count == 0)
            return BadRequest("Metadaten-Datei ist leer oder unvollständig.");

        var idx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headers.Count; i++) idx[headers[i].Trim('﻿', '"', ' ')] = i;

        int Get(string name) => idx.TryGetValue(name, out var i) ? i : -1;

        int colDokId       = Get("Dokument-ID");
        int colDateiname   = Get("Dateiname");
        int colKategorie   = Get("Kategorie");
        int colMandant     = Get("Mandant");
        int colVorname     = Get("Vorname");
        int colNachname    = Get("Nachname");
        int colGebDatum    = Get("Geburtsdatum");
        int colMaNummer    = Get("Mitarbeiter Nummer");
        int colMaCombined  = Get("Mitarbeiter (Name / Geb.-Datum)");
        int colBeschr      = Get("Beschreibung Dokument");
        int colErstelltAm  = Get("Erstellt am");
        int colGeaendertAm    = Get("Geändert am");
        int colDateiGeaendert = Get("Datei geändert am");
        int colZugriffAm      = Get("Zugriffsdatum");
        int colBesitzer       = Get("Im Besitz von");
        int colTypAbs      = Get("Dokumenttyp Absenzen");
        int colTypAem      = Get("Dokumenttyp Ämter & Behörden");
        int colTypPers     = Get("Dokumenttyp Persönliche Angaben");
        int colTypVert     = Get("Dokumenttyp Vertragsunterlagen");
        int colMime        = Get("MIME-Typ");
        int colGroesse     = Get("Größe");

        if (colDokId < 0 || colVorname < 0 || colNachname < 0 || colGebDatum < 0 || colKategorie < 0)
            return BadRequest("Metadaten-Datei hat nicht die erwarteten Spalten (Dokument-ID, Vorname, Nachname, Geburtsdatum, Kategorie).");

        // ZIP indexieren: XG-ID → Entry. Endung egal (PDF, DOC, DOCX, JPG, …).
        // Wichtig: Manche d.velop-Filenames enthalten MEHRERE XG-IDs (z.B.
        // "Foo (XG00007923).PDF (XG00008082).PDF" — alte Dokument-Verlinkung).
        // Daher ALLE XG-Matches als Schlüssel anlegen, nicht nur den ersten.
        using var zipStream = zipFile.OpenReadStream();
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);
        var xgRegex = new Regex(@"XG(\d+)", RegexOptions.IgnoreCase);
        var zipByXg = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        var zipEntryNames = new List<string>(); // für Diagnose
        foreach (var entry in zip.Entries)
        {
            zipEntryNames.Add(entry.FullName);
            // Alle XG-IDs im Filename sammeln (Name + FullName als Fallback)
            var matches = xgRegex.Matches(entry.Name);
            if (matches.Count == 0) matches = xgRegex.Matches(entry.FullName);
            foreach (Match m in matches)
            {
                var key = "XG" + m.Groups[1].Value;
                // Erstes Match pro Key gewinnt (falls dieselbe ID in mehreren Files steht)
                if (!zipByXg.ContainsKey(key)) zipByXg[key] = entry;
            }
        }

        // Cache: Branches, Mitarbeiter, Taxonomie
        var branches = await _db.CompanyProfiles
            .Select(c => new { c.Id, c.RestaurantCode, c.BranchName })
            .ToListAsync();
        var employees = await _db.Employees
            .Select(e => new { e.Id, e.EmployeeNumber, e.FirstName, e.LastName, e.DateOfBirth })
            .ToListAsync();
        var kategorien = await _db.DokumentKategorien.Where(k => k.Aktiv).ToListAsync();
        var typen = await _db.DokumentTypen.Where(t => t.Aktiv).ToListAsync();

        // Vorhandene Documents pro Employee zur Duplikat-Erkennung.
        // Walter-Bug 10.07.2026: d.velop erlaubt MEHRERE Dokumente mit gleichem
        // Dateinamen (unterscheidbar nur per XG-ID) — der reine Dateinamen-Check
        // verschluckte das zweite Dokument («22 statt 23»). Daher XG-ID mitladen
        // und bevorzugt darüber deduplizieren.
        var existingDocs = (await _db.EmployeeDokumente
            .Select(d => new { d.EmployeeId, d.FilenameOriginal, d.DvelopDokumentId, d.BranchCode, d.FilenameStorage })
            .ToListAsync())
            .Select(d => (d.EmployeeId, d.FilenameOriginal, d.DvelopDokumentId, d.BranchCode, d.FilenameStorage))
            .ToList();

        // Inhalt-Vergleich (Walter-Frage 10.07.2026 «ist der Inhalt wirklich
        // unterschiedlich?»): bei Namensgleichheit mit ANDERER/fehlender XG-ID
        // wird die ZIP-Datei per SHA-256 gegen die gespeicherte Datei verglichen —
        // identischer Inhalt = echtes d.velop-Duplikat (skip), unterschiedlicher
        // Inhalt = zweites eigenständiges Dokument (importieren).
        string? HashZipEntry(System.IO.Compression.ZipArchiveEntry e)
        {
            try
            {
                using var sha = System.Security.Cryptography.SHA256.Create();
                using var s = e.Open();
                return Convert.ToHexString(sha.ComputeHash(s));
            }
            catch { return null; }
        }
        string? HashStoredFile(int empId, string? branchCode, string? storageName)
        {
            try
            {
                if (string.IsNullOrEmpty(branchCode) || string.IsNullOrEmpty(storageName)) return null;
                var p = Path.Combine(_storagePath, branchCode, empId.ToString(), storageName);
                if (!System.IO.File.Exists(p)) return null;
                using var sha = System.Security.Cryptography.SHA256.Create();
                using var s = System.IO.File.OpenRead(p);
                return Convert.ToHexString(sha.ComputeHash(s));
            }
            catch { return null; }
        }

        result.TotalRows = dataRows.Count;

        for (int i = 0; i < dataRows.Count; i++)
        {
            var fields = dataRows[i];
            string F(int c) => c >= 0 && c < fields.Count ? (fields[c] ?? "").Trim() : "";

            var row = new DvelopRow {
                RowNum = i + 1,   // 1-basiert für UI-Anzeige (Zeile 1 = erste Datenzeile)
                XgId = F(colDokId).Trim('"'),
                Filename = F(colDateiname).Trim('"'),
                EmployeeName = $"{F(colVorname)} {F(colNachname)}".Trim(),
                Bemerkung = F(colBeschr),
                GueltigVon = ParseDate(F(colErstelltAm))
            };
            // Geburtsdatum: erst aus eigener Spalte, sonst aus Combined-Feld
            row.DateOfBirth = ParseDate(F(colGebDatum));
            if (row.DateOfBirth is null)
            {
                var combined = F(colMaCombined);
                var dateMatch = Regex.Match(combined, @"(\d{4}-\d{2}-\d{2}|\d{2}\.\d{2}\.\d{4})");
                if (dateMatch.Success) row.DateOfBirth = ParseDate(dateMatch.Value);
            }

            // Ziel-MA bestimmen (Walter-Vorgabe 10.07.2026, Massen-Modus):
            //   1. per-Row-Override aus dem Preview (manuelle Wahl gewinnt immer)
            //   2. PRO-ZEILE-Auflösung aus den CSV-Spalten (Nummer inkl. Aliase,
            //      sonst Name+Geburtsdatum) — macht Multi-MA-Exporte importierbar
            //   3. Fallback: der global vorgewählte MA (bisheriges Verhalten)
            var csvVorname  = F(colVorname);
            var csvNachname = F(colNachname);
            var csvMaNr     = F(colMaNummer);
            Employee? targetEmp = null;
            if (overrides.TryGetValue(row.XgId, out var overrideEmpId)
                && overrideEmpsById.TryGetValue(overrideEmpId, out var ovEmp))
            {
                targetEmp = ovEmp;
            }
            targetEmp ??= ResolveRowEmployee(csvMaNr, csvVorname, csvNachname, row.DateOfBirth);
            targetEmp ??= selectedEmp;
            if (targetEmp == null)
            {
                row.Action = "skip-no-employee";
                row.Reason = $"MA nicht zuordenbar: {csvVorname} {csvNachname}{(string.IsNullOrEmpty(csvMaNr) ? "" : $" (Nr. {csvMaNr})")} — bitte Ziel-MA in der Zeile wählen.";
                result.SkippedNoEmployee++;
                result.Preview.Add(row);
                continue;
            }
            row.EmployeeId = targetEmp.Id;
            bool sanityWarning = !string.IsNullOrEmpty(csvVorname) &&
                                 !targetEmp.FirstName.Equals(csvVorname, StringComparison.OrdinalIgnoreCase);

            // 2) Filiale aus Mandant ("58 McDonald's Restaurant Oftringen" → 058)
            var mandant = F(colMandant);
            var mandantNum = Regex.Match(mandant, @"^\s*(\d+)").Groups[1].Value;
            if (!string.IsNullOrEmpty(mandantNum))
            {
                var rc = mandantNum.PadLeft(3, '0');
                var br = branches.FirstOrDefault(b => b.RestaurantCode == rc);
                if (br != null) row.BranchCode = rc;
            }
            if (row.BranchCode == null)
            {
                row.Action = "skip-no-branch";
                row.Reason = $"Filiale aus '{mandant}' nicht gefunden";
                result.SkippedNoBranch++;
                result.Preview.Add(row);
                continue;
            }

            // 3) Kategorie + Typ matchen
            var kategorieRaw = F(colKategorie).Replace("HR:", "").Trim();
            var kat = MatchKategorie(kategorieRaw, kategorien);
            if (kat == null)
            {
                row.Action = "skip-no-category";
                row.Reason = $"Kategorie '{kategorieRaw}' nicht in unserer Taxonomie";
                result.SkippedNoCategory++;
                result.Preview.Add(row);
                continue;
            }
            row.KategorieName = kat.Name;

            // Sub-Typ aus passender Spalte
            string typRaw = kat.Name switch {
                "Absenzen"             => F(colTypAbs),
                "Ämter & Behörden"     => F(colTypAem),
                "Persönliche Angaben"  => F(colTypPers),
                "Vertragsunterlagen"   => F(colTypVert),
                _ => ""
            };
            var typ = MatchTyp(typRaw, kat.Id, typen);
            // Falls Typ leer oder nicht gefunden: Fallback auf "Diverses" der Kategorie
            if (typ == null)
            {
                typ = typen.FirstOrDefault(t => t.KategorieId == kat.Id && t.Name == "Diverses");
            }
            if (typ == null)
            {
                row.Action = "skip-no-category";
                row.Reason = $"Typ '{typRaw}' nicht gefunden, kein 'Diverses' als Fallback";
                result.SkippedNoCategory++;
                result.Preview.Add(row);
                continue;
            }
            row.TypName = typ.Name;
            row.DokumentTypId = typ.Id;

            // 4) ZIP-Entry finden via XG-ID
            if (!zipByXg.TryGetValue(row.XgId, out var entry))
            {
                // Diagnose: such ähnliche Filenames im ZIP (mit denselben letzten 4 Ziffern)
                var idTail = row.XgId.Length >= 4 ? row.XgId.Substring(row.XgId.Length - 4) : row.XgId;
                var similar = zipEntryNames
                    .Where(n => n.Contains(idTail, StringComparison.OrdinalIgnoreCase))
                    .Take(3)
                    .ToList();
                row.Action = "skip-no-file";
                row.Reason = similar.Any()
                    ? $"Datei mit ID {row.XgId} nicht erkannt. Ähnliche im ZIP: {string.Join(" | ", similar)}"
                    : $"Datei mit ID {row.XgId} nicht im ZIP (kein Filename mit {idTail} gefunden)";
                result.SkippedNoFile++;
                result.Preview.Add(row);
                continue;
            }

            // 5) Duplikat-Check pro tatsächlichem Ziel-MA (Walter-Bug 10.07.2026):
            //    BEVORZUGT über die d.velop-XG-ID (eindeutig). Der Dateinamen-
            //    Fallback greift nur noch, wenn eine der beiden Seiten KEINE
            //    XG-ID hat (Alt-Importe vor 06.06.2026 / CSV ohne ID) — zwei
            //    VERSCHIEDENE Dokumente mit gleichem Namen werden so beide importiert.
            var fnOrig = string.IsNullOrEmpty(row.Filename) ? entry.Name : row.Filename;
            var rowXg  = (row.XgId ?? "").Trim();

            // a) XG-ID bereits importiert → sicher dasselbe Dokument.
            if (rowXg.Length > 0 && existingDocs.Any(d => d.EmployeeId == targetEmp.Id
                    && !string.IsNullOrEmpty(d.DvelopDokumentId)
                    && string.Equals(d.DvelopDokumentId, rowXg, StringComparison.OrdinalIgnoreCase)))
            {
                row.Action = "skip-duplicate";
                row.Reason = $"Schon vorhanden (XG-ID): {fnOrig}";
                result.SkippedDuplicate++;
                result.Preview.Add(row);
                continue;
            }

            // b) Gleicher Dateiname beim selben MA, aber andere/fehlende XG-ID →
            //    INHALT vergleichen (SHA-256). Identisch = Duplikat, sonst import.
            var nameTwins = existingDocs.Where(d => d.EmployeeId == targetEmp.Id
                                                 && d.FilenameOriginal == fnOrig).ToList();
            if (nameTwins.Count > 0)
            {
                var zipHash = HashZipEntry(entry);
                var identical = zipHash != null && nameTwins.Any(d =>
                    HashStoredFile(d.EmployeeId, d.BranchCode, d.FilenameStorage) == zipHash);
                if (identical || zipHash == null)
                {
                    row.Action = "skip-duplicate";
                    row.Reason = identical
                        ? $"Schon vorhanden (Inhalt identisch): {fnOrig}"
                        : $"Schon vorhanden (gleicher Name, Inhalt nicht prüfbar): {fnOrig}";
                    result.SkippedDuplicate++;
                    result.Preview.Add(row);
                    continue;
                }
                row.Reason = $"Hinweis: gleicher Dateiname wie bestehendes Dokument, aber ANDERER Inhalt — wird als eigenes Dokument importiert.";
            }

            // 6) Importieren!
            row.Action = "create";
            if (sanityWarning)
                row.Reason = $"Hinweis: CSV-Name ({csvVorname} {csvNachname}) ≠ Ziel-MA ({targetEmp.FirstName} {targetEmp.LastName})";
            if (!dryRun)
            {
                var ext = Path.GetExtension(fnOrig);
                if (string.IsNullOrEmpty(ext)) ext = ".pdf";
                var storageName = Guid.NewGuid().ToString("N") + ext;

                var empDir = Path.Combine(_storagePath, row.BranchCode!, targetEmp.Id.ToString());
                Directory.CreateDirectory(empDir);
                var fullPath = Path.Combine(empDir, storageName);

                await using (var inS = entry.Open())
                await using (var outS = System.IO.File.Create(fullPath))
                {
                    await inS.CopyToAsync(outS);
                }

                var doc = new EmployeeDokument {
                    EmployeeId = targetEmp.Id,
                    DokumentTypId = typ.Id,
                    BranchCode = row.BranchCode,
                    FilenameOriginal = fnOrig,
                    FilenameStorage = storageName,
                    MimeType = string.IsNullOrEmpty(F(colMime)) ? "application/pdf" : F(colMime),
                    GroesseBytes = long.TryParse(F(colGroesse), out var sz) ? sz : new FileInfo(fullPath).Length,
                    Bemerkung = string.IsNullOrWhiteSpace(row.Bemerkung) ? null : row.Bemerkung,
                    GueltigVon = row.GueltigVon,
                    HochgeladenVon = GetCurrentUserId(),
                    HochgeladenAm = DateTime.UtcNow,
                    // d.velop-Metadaten 1:1 übernehmen (Walter-Vorgabe 24.05.2026).
                    ErstelltAm        = ParseDateTime(F(colErstelltAm)),
                    GeaendertAm       = ParseDateTime(F(colGeaendertAm)),
                    DateiGeaendertAm  = ParseDateTime(F(colDateiGeaendert)),
                    ZugriffAm         = ParseDateTime(F(colZugriffAm)),
                    GeaendertVon      = string.IsNullOrWhiteSpace(F(colBesitzer)) ? null : F(colBesitzer),
                    // Walter-Vorgabe 06.06.2026: d.velop-XG-ID gleich mitspeichern,
                    // damit künftige Metadaten-Backfills direkt per XG-ID matchen
                    // (eindeutig — d.velop erlaubt mehrere Dokumente mit gleichem
                    //  Dateinamen, die sich nur via XG-ID unterscheiden lassen).
                    DvelopDokumentId  = string.IsNullOrWhiteSpace(F(colDokId)) ? null : F(colDokId).Trim()
                };
                _db.EmployeeDokumente.Add(doc);
                await _db.SaveChangesAsync();

                // In existing-Cache aufnehmen für nachfolgende Zeilen
                existingDocs.Add((targetEmp.Id, fnOrig, doc.DvelopDokumentId, doc.BranchCode, doc.FilenameStorage));
            }
            result.Imported++;
            result.Preview.Add(row);
        }

        return Ok(result);
    }

    // ──────────────────────────────────────────────────────────────────
    // DATEI-IMPORT OHNE CSV (Walter-Vorgabe 11.07.2026)
    // Problem: d.velop liefert die Metadaten-CSV nur pro MA, und das Filial-ZIP
    // ist >1 GB. Lösung: der Massen-Download (lose Dateien, XG-ID im Namen)
    // wird DATEI FÜR DATEI hochgeladen — kein Grössenlimit, Fortschritt im UI.
    // Pro Datei: XG-ID aus dem Namen (Dedupe), MA per Namens-Token-Match
    // (alle Vor- UND Nachnamen-Tokens müssen im Dateinamen vorkommen),
    // Inhalts-Hash gegen die bestehenden Dokumente des MA, Dokumenttyp per
    // Substring-Match über die Taxonomie (Fallback «Diverses»).
    // ──────────────────────────────────────────────────────────────────

    [HttpPost("file-import")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = 500_000_000)]
    public async Task<IActionResult> FileImport(
        [FromForm] IFormFile file,
        [FromForm] int? employeeId = null,
        [FromForm] string? branchCode = null)
    {
        if (file == null || file.Length == 0) return BadRequest("Datei fehlt.");
        var rawName = Path.GetFileName(file.FileName ?? "datei");

        // 1) XG-ID aus dem Namen — primärer Dedupe-Schlüssel.
        var xg = Regex.Match(rawName, @"\((XG\d+)\)", RegexOptions.IgnoreCase).Groups[1].Value.ToUpperInvariant();
        if (xg.Length > 0 && await _db.EmployeeDokumente.AnyAsync(d => d.DvelopDokumentId == xg))
            return Ok(new { status = "skip", reason = "Schon vorhanden (XG-ID)", xgId = xg, filename = rawName });

        // «Sauberer» Original-Dateiname ohne XG-Zusatz.
        var fnOrig = Regex.Replace(rawName, @"\s*\((XG\d+)\)", "", RegexOptions.IgnoreCase).Trim();

        // 2) MA bestimmen: explizite Wahl > Namens-Token-Match über den Dateinamen.
        Employee? emp = null;
        if (employeeId is > 0)
            emp = await _db.Employees.Include(e => e.Employments).ThenInclude(em => em.CompanyProfile)
                .FirstOrDefaultAsync(e => e.Id == employeeId.Value);
        if (emp == null)
        {
            var nameTokens = Regex.Matches(fnOrig.ToLowerInvariant(), @"[\p{L}]{2,}")
                .Select(m => m.Value).ToHashSet();
            var all = await _db.Employees
                .Include(e => e.Employments).ThenInclude(em => em.CompanyProfile)
                .Where(e => !e.IsHidden)
                .ToListAsync();
            bool TokensIn(string? s) => !string.IsNullOrWhiteSpace(s)
                && Regex.Matches(s.ToLowerInvariant(), @"[\p{L}]{2,}")
                    .All(m => nameTokens.Contains(m.Value));
            var hits = all.Where(e => TokensIn(e.FirstName) && TokensIn(e.LastName)).ToList();
            if (hits.Count == 1) emp = hits[0];
            else
                return Ok(new
                {
                    status = "needs-employee",
                    reason = hits.Count == 0
                        ? "Kein MA-Name im Dateinamen erkannt — bitte MA wählen."
                        : $"{hits.Count} MA passen ({string.Join(", ", hits.Take(4).Select(h => h.FirstName + " " + h.LastName))}) — bitte wählen.",
                    xgId = xg, filename = rawName,
                });
        }

        // 3) Inhalt lesen + Hash-Dedupe gegen bestehende Dokumente DIESES MA.
        byte[] bytes;
        using (var ms = new MemoryStream()) { await file.CopyToAsync(ms); bytes = ms.ToArray(); }
        string hash;
        using (var sha = System.Security.Cryptography.SHA256.Create())
            hash = Convert.ToHexString(sha.ComputeHash(bytes));
        var empDocs = await _db.EmployeeDokumente
            .Where(d => d.EmployeeId == emp.Id)
            .Select(d => new { d.BranchCode, d.FilenameStorage })
            .ToListAsync();
        foreach (var d in empDocs)
        {
            try
            {
                if (string.IsNullOrEmpty(d.BranchCode) || string.IsNullOrEmpty(d.FilenameStorage)) continue;
                var p = Path.Combine(_storagePath, d.BranchCode, emp.Id.ToString(), d.FilenameStorage);
                if (!System.IO.File.Exists(p)) continue;
                using var sha2 = System.Security.Cryptography.SHA256.Create();
                using var fs = System.IO.File.OpenRead(p);
                if (Convert.ToHexString(sha2.ComputeHash(fs)) == hash)
                    return Ok(new { status = "skip", reason = "Schon vorhanden (Inhalt identisch)", xgId = xg, filename = rawName, employee = $"{emp.FirstName} {emp.LastName}" });
            }
            catch { /* Datei nicht lesbar → weiter */ }
        }

        // 4) Dokumenttyp: längster Taxonomie-Name, der im Dateinamen vorkommt;
        //    Fallback «Diverses».
        var typen = await _db.DokumentTypen.Where(ty => ty.Aktiv).ToListAsync();
        var fnLower = fnOrig.ToLowerInvariant();
        DokumentTyp? typ = null;
        foreach (var ty in typen)
        {
            var n = (ty.Name ?? "").Trim().ToLowerInvariant();
            if (n.Length >= 4 && fnLower.Contains(n) && (typ == null || n.Length > typ.Name.Length))
                typ = ty;
        }
        typ ??= typen.FirstOrDefault(ty => string.Equals(ty.Name, "Diverses", StringComparison.OrdinalIgnoreCase));
        if (typ == null)
            return Ok(new { status = "skip", reason = "Kein Dokumenttyp zuordenbar (auch kein «Diverses» in der Taxonomie).", xgId = xg, filename = rawName });

        // 5) Filiale: neuester Vertrag des MA, sonst Parameter (globaler Selektor).
        var branch = emp.Employments?
            .Where(em => em.CompanyProfile?.RestaurantCode != null)
            .OrderByDescending(em => em.ContractStartDate)
            .Select(em => em.CompanyProfile!.RestaurantCode)
            .FirstOrDefault() ?? (string.IsNullOrWhiteSpace(branchCode) ? null : branchCode!.Trim());
        if (string.IsNullOrEmpty(branch))
            return Ok(new { status = "skip", reason = "Keine Filiale bestimmbar (MA ohne Vertrag) — bitte Filiale im Selektor wählen.", xgId = xg, filename = rawName });

        // 6) Speichern.
        var ext = Path.GetExtension(fnOrig);
        if (string.IsNullOrEmpty(ext)) ext = ".pdf";
        var storageName = Guid.NewGuid().ToString("N") + ext;
        var dir = Path.Combine(_storagePath, branch, emp.Id.ToString());
        Directory.CreateDirectory(dir);
        await System.IO.File.WriteAllBytesAsync(Path.Combine(dir, storageName), bytes);

        _db.EmployeeDokumente.Add(new EmployeeDokument
        {
            EmployeeId = emp.Id,
            DokumentTypId = typ.Id,
            BranchCode = branch,
            FilenameOriginal = fnOrig,
            FilenameStorage = storageName,
            MimeType = ext.ToLowerInvariant() switch
            {
                ".pdf" => "application/pdf",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".doc" => "application/msword",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                _ => "application/octet-stream"
            },
            GroesseBytes = bytes.LongLength,
            HochgeladenVon = GetCurrentUserId(),
            HochgeladenAm = DateTime.UtcNow,
            DvelopDokumentId = xg.Length > 0 ? xg : null,
        });
        await _db.SaveChangesAsync();

        return Ok(new
        {
            status = "imported",
            filename = rawName,
            xgId = xg,
            employee = $"{emp.FirstName} {emp.LastName}",
            employeeId = emp.Id,
            typ = typ.Name,
            branch,
        });
    }

    // ──────────────────────────────────────────────────────────────────
    // DATEI-IMPORT MIT EXCEL-METADATEN (Walter-Vorgabe 11.07.2026)
    // Der d.3one-Excel-Export (gleiche Spalten wie der CSV-Import) liefert die
    // ECHTEN Metadaten (Kategorie, Typ-Spalten, Beschreibung, MA, Datumsfelder);
    // die zugehörige Datei kommt aus dem lokalen Massen-Download-Ordner und
    // wird vom Frontend per XG-ID im Dateinamen gefunden und einzeln
    // hochgeladen. Dieser Endpoint verarbeitet EINE Datei + ihre Excel-Zeile —
    // identische Zuordnungslogik wie der CSV+ZIP-Import, ohne ZIP-Limit.
    // ──────────────────────────────────────────────────────────────────

    [HttpPost("file-import-meta")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = 500_000_000)]
    public async Task<IActionResult> FileImportMeta(
        [FromForm] IFormFile file,
        [FromForm] string meta,
        [FromForm] int? employeeId = null,
        [FromForm] string? branchCode = null)
    {
        if (file == null || file.Length == 0) return BadRequest("Datei fehlt.");
        Dictionary<string, string> m;
        try
        {
            m = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(meta)
                ?? new Dictionary<string, string>();
        }
        catch { return BadRequest("Metadaten (meta) sind kein gültiges JSON."); }
        string M(string key) => m.TryGetValue(key, out var v) ? (v ?? "").Trim() : "";

        var rawName = Path.GetFileName(file.FileName ?? "datei");
        var xg = M("Dokument-ID");
        if (string.IsNullOrEmpty(xg))
            xg = Regex.Match(rawName, @"\((XG\d+)\)", RegexOptions.IgnoreCase).Groups[1].Value;
        xg = xg.ToUpperInvariant();

        // 1) Dedupe per XG-ID.
        if (xg.Length > 0 && await _db.EmployeeDokumente.AnyAsync(d => d.DvelopDokumentId == xg))
            return Ok(new { status = "skip", reason = "Schon vorhanden (XG-ID)", xgId = xg, filename = rawName });

        // Original-Dateiname: aus dem Excel («Dateiname»), sonst Upload-Name ohne XG-Zusatz.
        var fnOrig = M("Dateiname");
        if (string.IsNullOrEmpty(fnOrig))
            fnOrig = Regex.Replace(rawName, @"\s*\((XG\d+)\)", "", RegexOptions.IgnoreCase).Trim();

        // 2) Kategorie + Typ — exakt wie der CSV-Import (generische Typ-Spalte
        //    «Dokumenttyp {Kategorie}», Fallback «Diverses» der Kategorie).
        var kategorien = await _db.DokumentKategorien.Where(k => k.Aktiv).ToListAsync();
        var typen = await _db.DokumentTypen.Where(ty => ty.Aktiv).ToListAsync();
        var kategorieRaw = M("Kategorie").Replace("HR:", "").Trim();
        var kat = MatchKategorie(kategorieRaw, kategorien);
        if (kat == null)
            return Ok(new { status = "skip", reason = $"Kategorie «{kategorieRaw}» nicht in unserer Taxonomie", xgId = xg, filename = rawName });
        var typRaw = M($"Dokumenttyp {kat.Name}");
        var typ = MatchTyp(typRaw, kat.Id, typen)
                  ?? typen.FirstOrDefault(ty => ty.KategorieId == kat.Id && ty.Name == "Diverses");
        if (typ == null)
            return Ok(new { status = "skip", reason = $"Typ «{typRaw}» nicht gefunden, kein «Diverses» in Kategorie {kat.Name}", xgId = xg, filename = rawName });

        // 3) MA: explizite Wahl > Nummer (inkl. Alias, «alt»-tolerant) > Name+Geb.
        Employee? emp = null;
        if (employeeId is > 0)
            emp = await _db.Employees.Include(e => e.Employments).ThenInclude(em => em.CompanyProfile)
                .FirstOrDefaultAsync(e => e.Id == employeeId.Value);
        if (emp == null)
        {
            static string NormN(string? n) => Regex.Replace((n ?? "").Trim(), "alt$", "", RegexOptions.IgnoreCase);
            var maNr = NormN(M("Mitarbeiter Nummer"));
            var all = await _db.Employees
                .Include(e => e.NumberAliases)
                .Include(e => e.Employments).ThenInclude(em => em.CompanyProfile)
                .Where(e => !e.IsHidden)
                .ToListAsync();
            if (maNr.Length > 0)
            {
                var byNum = all.Where(e => NormN(e.EmployeeNumber) == maNr
                        || e.NumberAliases.Any(a => NormN(a.Number) == maNr)).ToList();
                if (byNum.Count == 1) emp = byNum[0];
            }
            if (emp == null)
            {
                var vn = M("Vorname"); var nn = M("Nachname");
                var geb = ParseDate(M("Geburtsdatum"));
                if (vn.Length > 0 && nn.Length > 0)
                {
                    var hits = all.Where(e =>
                            string.Equals((e.FirstName ?? "").Trim(), vn, StringComparison.OrdinalIgnoreCase)
                         && string.Equals((e.LastName ?? "").Trim(), nn, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (hits.Count > 1 && geb.HasValue)
                        hits = hits.Where(e => e.DateOfBirth.HasValue
                            && DateOnly.FromDateTime(e.DateOfBirth.Value) == geb.Value).ToList();
                    if (hits.Count == 1) emp = hits[0];
                }
            }
            if (emp == null)
                return Ok(new
                {
                    status = "needs-employee",
                    reason = $"MA nicht zuordenbar: {M("Vorname")} {M("Nachname")} (Nr. {M("Mitarbeiter Nummer")})",
                    xgId = xg, filename = rawName,
                });
        }

        // 4) Inhalt + Hash-Dedupe gegen bestehende Dokumente dieses MA.
        byte[] bytes;
        using (var ms = new MemoryStream()) { await file.CopyToAsync(ms); bytes = ms.ToArray(); }
        string hash;
        using (var sha = System.Security.Cryptography.SHA256.Create())
            hash = Convert.ToHexString(sha.ComputeHash(bytes));
        var empDocs = await _db.EmployeeDokumente
            .Where(d => d.EmployeeId == emp.Id)
            .Select(d => new { d.BranchCode, d.FilenameStorage })
            .ToListAsync();
        foreach (var d in empDocs)
        {
            try
            {
                if (string.IsNullOrEmpty(d.BranchCode) || string.IsNullOrEmpty(d.FilenameStorage)) continue;
                var p = Path.Combine(_storagePath, d.BranchCode, emp.Id.ToString(), d.FilenameStorage);
                if (!System.IO.File.Exists(p)) continue;
                using var sha2 = System.Security.Cryptography.SHA256.Create();
                using var fs = System.IO.File.OpenRead(p);
                if (Convert.ToHexString(sha2.ComputeHash(fs)) == hash)
                    return Ok(new { status = "skip", reason = "Schon vorhanden (Inhalt identisch)", xgId = xg, filename = rawName, employee = $"{emp.FirstName} {emp.LastName}" });
            }
            catch { /* weiter */ }
        }

        // 5) Filiale: Mandant («58 McDonald's …» → 058) > neuester Vertrag > Selektor.
        string? branch = null;
        var mandantNum = Regex.Match(M("Mandant"), @"^\s*(\d+)").Groups[1].Value;
        if (!string.IsNullOrEmpty(mandantNum)) branch = mandantNum.PadLeft(3, '0');
        branch ??= emp.Employments?
            .Where(em => em.CompanyProfile?.RestaurantCode != null)
            .OrderByDescending(em => em.ContractStartDate)
            .Select(em => em.CompanyProfile!.RestaurantCode)
            .FirstOrDefault();
        branch ??= string.IsNullOrWhiteSpace(branchCode) ? null : branchCode!.Trim();
        if (string.IsNullOrEmpty(branch))
            return Ok(new { status = "skip", reason = "Keine Filiale bestimmbar (kein Mandant, MA ohne Vertrag).", xgId = xg, filename = rawName });

        // 6) Speichern — Metadaten 1:1 aus dem Excel (wie der CSV-Import).
        var ext = Path.GetExtension(fnOrig);
        if (string.IsNullOrEmpty(ext)) ext = Path.GetExtension(rawName);
        if (string.IsNullOrEmpty(ext)) ext = ".pdf";
        var storageName = Guid.NewGuid().ToString("N") + ext;
        var dir = Path.Combine(_storagePath, branch, emp.Id.ToString());
        Directory.CreateDirectory(dir);
        await System.IO.File.WriteAllBytesAsync(Path.Combine(dir, storageName), bytes);

        var beschreibung = M("Beschreibung Dokument");
        _db.EmployeeDokumente.Add(new EmployeeDokument
        {
            EmployeeId = emp.Id,
            DokumentTypId = typ.Id,
            BranchCode = branch,
            FilenameOriginal = fnOrig,
            FilenameStorage = storageName,
            MimeType = string.IsNullOrEmpty(M("MIME-Typ")) ? "application/pdf" : M("MIME-Typ"),
            GroesseBytes = bytes.LongLength,
            Bemerkung = string.IsNullOrWhiteSpace(beschreibung) ? null : beschreibung,
            GueltigVon = ParseDate(M("Erstellt am")),
            HochgeladenVon = GetCurrentUserId(),
            HochgeladenAm = DateTime.UtcNow,
            ErstelltAm = ParseDateTime(M("Erstellt am")),
            GeaendertAm = ParseDateTime(M("Geändert am")),
            DateiGeaendertAm = ParseDateTime(M("Datei geändert am")),
            ZugriffAm = ParseDateTime(M("Zugriffsdatum")),
            GeaendertVon = string.IsNullOrWhiteSpace(M("Im Besitz von")) ? null : M("Im Besitz von"),
            DvelopDokumentId = xg.Length > 0 ? xg : null,
        });
        await _db.SaveChangesAsync();

        return Ok(new
        {
            status = "imported",
            filename = rawName,
            xgId = xg,
            employee = $"{emp.FirstName} {emp.LastName}",
            employeeId = emp.Id,
            typ = typ.Name,
            kategorie = kat.Name,
            branch,
        });
    }

    // ──────────────────────────────────────────────────────────────────
    // METADATEN-BACKFILL (Walter-Vorgabe 24.05.2026)
    // Trägt zu BEREITS importierten Dokumenten die d.velop-Metadaten nach:
    // Erstellt / Geändert / Datei geändert / Zugriff + „Im Besitz von".
    // Braucht NUR die Excel/CSV (keine ZIP). Der Mitarbeiter wird pro Zeile
    // SELBST aufgelöst (Mitarbeiter Nummer, sonst Name + Geburtsdatum) — keine
    // MA-Vorauswahl nötig. Match auf das bestehende Dokument:
    //   EmployeeId + FilenameOriginal == Spalte „Dateiname"
    // (= derselbe Schlüssel wie der Import-Dedup → praktisch 1:1).
    // ──────────────────────────────────────────────────────────────────
    public class BackfillResult
    {
        public bool DryRun { get; set; }
        public int TotalRows { get; set; }
        public int Updated { get; set; }
        /// <summary>
        /// Walter-Vorgabe 06.06.2026: Zeilen, deren Excel-Werte mit dem DB-Stand
        /// IDENTISCH sind — kein „Update" mehr beim Re-Run. Vorher liefen die
        /// im `Updated`-Counter mit, was bei Re-Imports stets dieselbe Zahl gab.
        /// </summary>
        public int Unchanged { get; set; }
        public int NoEmployee { get; set; }
        public int NoDocument { get; set; }
        public int NoData { get; set; }
        public List<string> Unmatched { get; set; } = new();   // Beispiele für die UI (max 50)

        /// <summary>
        /// Walter-Vorgabe 06.06.2026: strukturierte Liste der Dokumente, die
        /// im d.velop-Export stehen, aber bei uns FEHLEN. Damit Walter sie
        /// gezielt nachimportieren kann (ZIP oder Einzel-Upload).
        /// </summary>
        public List<MissingDocument> MissingDocuments { get; set; } = new();

        /// <summary>
        /// Walter-Vorgabe 06.06.2026: strukturierte Liste der Zeilen, die
        /// aktualisiert WURDEN (oder bei dryRun würden). Damit Walter sieht,
        /// welche MA + welche Felder pro Zeile betroffen sind.
        /// Cap bei 1000 (Frontend-UI-Schutz).
        /// </summary>
        public List<UpdatedItem> UpdatedItems { get; set; } = new();

        /// <summary>
        /// Walter-Vorgabe 06.06.2026: Mitarbeiter, die im d.velop-Export stehen,
        /// aber bei uns nicht existieren — gruppiert pro MA mit Dokument-Count.
        /// Frontend bietet pro MA einen „Anlegen (Personaldossier)"-Button an,
        /// der die MA inaktiv, ohne Vertrag, anlegt (alt-Suffix bei Kollision).
        /// </summary>
        public List<MissingEmployee> MissingEmployees { get; set; } = new();
    }

    public class MissingEmployee
    {
        public string MaNr        { get; set; } = "";
        public string Vorname     { get; set; } = "";
        public string Nachname    { get; set; } = "";
        public string GeburtsIso  { get; set; } = "";   // "2006-10-02" oder leer
        public string Mandant     { get; set; } = "";   // d.velop-Mandant ("58 McDonald's …")
        public string BranchCode  { get; set; } = "";   // abgeleitet, z.B. "058"
        public string DvelopStatus{ get; set; } = "";   // "ausgetreten" / "aktiv"
        public int    DokumentCount { get; set; }
    }

    public class CreateMissingEmployeeDto
    {
        public string MaNr       { get; set; } = "";
        public string Vorname    { get; set; } = "";
        public string Nachname   { get; set; } = "";
        public string? GeburtsIso{ get; set; }
        public string? BranchCode{ get; set; }
        public bool   Inactive   { get; set; } = true;   // Default: inaktiv
        /// <summary>
        /// Wenn true, wird die Duplikat-Prüfung übersprungen (Walter hat
        /// bestätigt, dass es trotz Ähnlichkeit ein anderer MA ist).
        /// </summary>
        public bool   ForceCreate{ get; set; }
    }

    public class AssignToExistingDto
    {
        public int     ExistingDocId    { get; set; }
        public string  DvelopDokumentId { get; set; } = "";
        public string? ErstelltAm       { get; set; }   // ISO yyyy-MM-ddTHH:mm:ss
        public string? GeaendertAm      { get; set; }
        public string? DateiGeaendertAm { get; set; }
        public string? ZugriffAm        { get; set; }
        public string? GeaendertVon     { get; set; }
    }

    /// <summary>
    /// Walter-Vorgabe 06.06.2026: bestehendes DB-Dokument einer d.velop-Zeile
    /// zuordnen. Setzt die XG-ID + d.velop-Datumsfelder auf den existierenden
    /// Datensatz. KATEGORIE, TYP und BEMERKUNG bleiben unverändert — Walter
    /// hat sie ja bewusst angepasst, sonst wäre kein Zuordnen nötig.
    /// </summary>
    [HttpPost("assign-to-existing")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> AssignToExisting([FromBody] AssignToExistingDto dto)
    {
        if (dto.ExistingDocId <= 0 || string.IsNullOrWhiteSpace(dto.DvelopDokumentId))
            return BadRequest(new { error = "ExistingDocId und DvelopDokumentId sind Pflicht." });

        var doc = await _db.EmployeeDokumente.FindAsync(dto.ExistingDocId);
        if (doc is null) return NotFound(new { error = "Dokument nicht gefunden." });

        // Kollision: andere d.velop-XG-ID schon hinterlegt → nicht überschreiben
        if (!string.IsNullOrWhiteSpace(doc.DvelopDokumentId)
            && !string.Equals(doc.DvelopDokumentId, dto.DvelopDokumentId, StringComparison.OrdinalIgnoreCase))
        {
            return Conflict(new {
                error = "XG_ID_CONFLICT",
                message = $"Dieses Dokument ist bereits mit d.velop-ID '{doc.DvelopDokumentId}' verknüpft."
            });
        }

        DateTime? Iso(string? s)
            => DateTime.TryParseExact(s, "yyyy-MM-ddTHH:mm:ss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var d)
            ? DateTime.SpecifyKind(d, DateTimeKind.Unspecified)
            : (DateTime?)null;

        doc.DvelopDokumentId = dto.DvelopDokumentId.Trim();
        var e = Iso(dto.ErstelltAm);       if (e.HasValue) doc.ErstelltAm       = e;
        var g = Iso(dto.GeaendertAm);      if (g.HasValue) doc.GeaendertAm      = g;
        var d2= Iso(dto.DateiGeaendertAm); if (d2.HasValue) doc.DateiGeaendertAm = d2;
        var z = Iso(dto.ZugriffAm);        if (z.HasValue) doc.ZugriffAm        = z;
        if (!string.IsNullOrWhiteSpace(dto.GeaendertVon)) doc.GeaendertVon = dto.GeaendertVon.Trim();

        await _db.SaveChangesAsync();
        return Ok(new { id = doc.Id, dvelopDokumentId = doc.DvelopDokumentId });
    }

    /// <summary>
    /// Standard-Levenshtein-Distanz. Walter-Vorgabe 06.06.2026 für die
    /// Duplikat-Erkennung beim Missing-MA-Anlegen (z.B. „Iancu" vs „Lancu").
    /// </summary>
    private static int LevenshteinDistance(string a, string b)
    {
        if (string.IsNullOrEmpty(a)) return b?.Length ?? 0;
        if (string.IsNullOrEmpty(b)) return a.Length;
        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }
        return prev[b.Length];
    }

    /// <summary>
    /// Walter-Vorgabe 06.06.2026: legt einen fehlenden d.velop-MA als
    /// Personaldossier an (inaktiv, ohne Vertrag, ohne Lohn). Nutzt die
    /// MA-Nummer 1:1 falls verfügbar, sonst mit „alt"-Suffix bei Kollision
    /// (analog Archiv-Import). Damit die zugehörigen Dokumente per Quick-
    /// Upload / Backfill diesem MA zugeordnet werden können.
    /// </summary>
    [HttpPost("create-missing-employee")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> CreateMissingEmployee([FromBody] CreateMissingEmployeeDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Vorname) || string.IsNullOrWhiteSpace(dto.Nachname))
            return BadRequest(new { error = "Vorname und Nachname sind Pflicht." });

        // Walter-Vorgabe 06.06.2026: Pre-Check für mögliche Duplikate. Wenn ein
        // MA mit derselben MA-Nr ODER demselben Geburtsdatum existiert UND der
        // Nachname per Lev≤2-Fuzzy-Match ähnlich ist, brechen wir ab und liefern
        // 409 mit Hinweis. Verhindert Duplikate wie „Emanuel-Lancu" vs
        // „Emanuel-Iancu" (visuell identisch — I vs l).
        DateTime? gebPre = null;
        if (!string.IsNullOrWhiteSpace(dto.GeburtsIso)
            && DateTime.TryParseExact(dto.GeburtsIso, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var gPre))
        {
            gebPre = gPre.Date;
        }
        var allEmps = await _db.Employees
            .Select(e => new { e.Id, e.EmployeeNumber, e.FirstName, e.LastName, e.DateOfBirth })
            .ToListAsync();
        var nrTrim = (dto.MaNr ?? "").Trim();
        var fuzzy = allEmps.Where(e =>
            (gebPre.HasValue && e.DateOfBirth.HasValue && e.DateOfBirth.Value.Date == gebPre.Value)
            || (!string.IsNullOrWhiteSpace(nrTrim) && string.Equals(e.EmployeeNumber, nrTrim, StringComparison.OrdinalIgnoreCase))
        ).Where(e =>
            LevenshteinDistance((e.LastName ?? "").ToLowerInvariant(), dto.Nachname.Trim().ToLowerInvariant()) <= 2
            && LevenshteinDistance((e.FirstName ?? "").ToLowerInvariant(), dto.Vorname.Trim().ToLowerInvariant()) <= 2
        ).FirstOrDefault();
        if (fuzzy != null && !dto.ForceCreate)
        {
            return Conflict(new {
                error          = "POSSIBLE_DUPLICATE",
                message        = $"Es existiert bereits ein ähnlicher Mitarbeiter: {fuzzy.FirstName} {fuzzy.LastName} (Nr. {fuzzy.EmployeeNumber}). Verwende denselben — oder trotzdem anlegen.",
                existingId     = fuzzy.Id,
                existingNr     = fuzzy.EmployeeNumber,
                existingName   = $"{fuzzy.FirstName} {fuzzy.LastName}".Trim(),
                existingGeb    = fuzzy.DateOfBirth?.ToString("yyyy-MM-dd")
            });
        }

        // MA-Nummer ermitteln: zuerst MaNr aus DTO, bei Kollision alt-Suffix.
        var baseNr = (dto.MaNr ?? "").Trim();
        if (string.IsNullOrWhiteSpace(baseNr)) baseNr = "";

        string finalNr = baseNr;
        if (!string.IsNullOrWhiteSpace(baseNr))
        {
            var exists = await _db.Employees.AnyAsync(e => e.EmployeeNumber == baseNr);
            if (exists)
            {
                // suffix-Schleife: 580015alt, 580015alt2, …
                string suffix = "alt";
                int n = 1;
                while (true)
                {
                    var candidate = baseNr + (n == 1 ? suffix : suffix + n);
                    if (!await _db.Employees.AnyAsync(e => e.EmployeeNumber == candidate))
                    {
                        finalNr = candidate;
                        break;
                    }
                    n++;
                    if (n > 99) return Conflict(new { error = "Zu viele Kollisionen (>99 alt-Suffixe)." });
                }
            }
        }

        DateTime? geb = null;
        if (!string.IsNullOrWhiteSpace(dto.GeburtsIso)
            && DateTime.TryParseExact(dto.GeburtsIso, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var g))
        {
            geb = DateTime.SpecifyKind(g, DateTimeKind.Unspecified);
        }

        var emp = new Employee
        {
            EmployeeNumber       = string.IsNullOrWhiteSpace(finalNr) ? null : finalNr,
            FirstName            = dto.Vorname.Trim(),
            LastName             = dto.Nachname.Trim(),
            DateOfBirth          = geb,
            IsActive             = !dto.Inactive,
            // Walter-Vorgabe 06.06.2026: aus d.velop importierte Alt-MA bekommen
            // kein Lohn-Setup — Personaldossier-Modus, keine Filial-Zuordnung
            // (kommt erst, wenn / falls je ein Vertrag erfasst wird).
            IsPayrollExcluded    = false,
            EntryDate            = null,
            ExitDate             = dto.Inactive ? DateTime.Today : (DateTime?)null,
            Country              = "CH"
        };

        _db.Employees.Add(emp);
        await _db.SaveChangesAsync();

        return Ok(new {
            id            = emp.Id,
            employeeNumber= emp.EmployeeNumber,
            firstName     = emp.FirstName,
            lastName      = emp.LastName,
            usedAltSuffix = !string.Equals(finalNr, baseNr, StringComparison.OrdinalIgnoreCase),
            originalNr    = baseNr,
            finalNr       = finalNr
        });
    }

    public class UpdatedItem
    {
        public string MaNr             { get; set; } = "";
        public string MaName           { get; set; } = "";
        public string Filename         { get; set; } = "";
        public string Beschreibung     { get; set; } = "";
        public List<string> ChangedFields { get; set; } = new();   // z.B. ["Erstellt", "Geändert", "Geöffnet"]
    }

    public class MissingDocument
    {
        public string MaNr        { get; set; } = "";
        public string MaName      { get; set; } = "";
        public string Filename    { get; set; } = "";
        public string Kategorie   { get; set; } = "";
        public string Typ         { get; set; } = "";
        public string Beschreibung{ get; set; } = "";
        public string DokumentId  { get; set; } = "";
        public string Url         { get; set; } = "";
        // Walter-Vorgabe 06.06.2026: schon im Backend auflösen, damit der
        // Direkt-Upload aus der „fehlende Dokumente"-Liste keine zusätzliche
        // Lookup-Logik im Frontend braucht.
        public int    EmployeeId    { get; set; }
        public int?   DokumentTypId { get; set; }   // null wenn Kategorie/Typ nicht aufgelöst
        public string BranchCode    { get; set; } = "";
        // Walter-Vorgabe 06.06.2026: d.velop-Datumsfelder mitschleifen, damit
        // der Schnell-Upload sie als Metadaten am neuen Dokument setzen kann.
        // Format: ISO-String („2025-12-22T08:38:05") oder leer.
        public string? ErstelltAm       { get; set; }
        public string? GeaendertAm      { get; set; }
        public string? DateiGeaendertAm { get; set; }
        public string? ZugriffAm        { get; set; }
        public string? GeaendertVon     { get; set; }
    }

    [HttpPost("backfill-metadata")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = 2_000_000_000)]
    public async Task<IActionResult> BackfillMetadata(
        [FromForm] IFormFile csvFile,
        [FromForm] bool dryRun = true)
    {
        if (csvFile == null || csvFile.Length == 0)
            return BadRequest("Metadaten-Datei (CSV oder XLSX) fehlt.");

        List<string> headers; List<List<string>> dataRows;
        try { (headers, dataRows) = ReadMetadataFile(csvFile); }
        catch (Exception ex) { return BadRequest($"Datei konnte nicht gelesen werden: {ex.Message}"); }
        if (headers.Count == 0 || dataRows.Count == 0)
            return BadRequest("Datei ist leer oder unvollständig.");

        var idx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headers.Count; i++) idx[headers[i].Trim('﻿', '"', ' ')] = i;
        int Get(string n) => idx.TryGetValue(n, out var i) ? i : -1;

        int colDateiname      = Get("Dateiname");
        int colMaNummer       = Get("Mitarbeiter Nummer");
        int colMaCombined     = Get("Mitarbeiter (Name / Geb.-Datum)");
        int colVorname        = Get("Vorname");
        int colNachname       = Get("Nachname");
        int colGebDatum       = Get("Geburtsdatum");
        // Walter-Vorgabe 06.06.2026: zusätzliche Spalten für die „Fehlt"-Liste,
        // damit beim Nicht-Match alle Infos zur Identifikation vorhanden sind.
        int colKategorie      = Get("Kategorie");
        int colBeschr         = Get("Beschreibung Dokument");
        int colDokId          = Get("Dokument-ID");
        int colUrl            = Get("URL zum Element");
        int colMandant_BF     = Get("Mandant");
        int colMaStatus       = Get("Mitarbeiter Status");
        // d.velop fügt Dokumenttyp-Spalten DYNAMISCH pro Export hinzu (nur für
        // Kategorien, die in den Treffern vorkommen). Wir bauen daher eine Map
        // „Kategorie → Spalten-Index" basierend auf allen Headern, die mit
        // „Dokumenttyp " beginnen. Damit greifen auch neue Kategorien (z.B.
        // „Mitarbeiterentwicklung") ohne Code-Anpassung. Walter 06.06.2026.
        var typColByKategorie = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int hi = 0; hi < headers.Count; hi++)
        {
            var hh = headers[hi].Trim('﻿', '"', ' ');
            if (hh.StartsWith("Dokumenttyp ", StringComparison.OrdinalIgnoreCase))
                typColByKategorie[hh.Substring("Dokumenttyp ".Length).Trim()] = hi;
        }
        int colErstelltAm     = Get("Erstellt am");
        int colGeaendertAm    = Get("Geändert am");
        int colDateiGeaendert = Get("Datei geändert am");
        int colZugriffAm      = Get("Zugriffsdatum");
        int colBesitzer       = Get("Im Besitz von");

        if (colDateiname < 0)
            return BadRequest("Spalte Dateiname fehlt - ohne sie ist keine Zuordnung zum Dokument moeglich.");

        // Mitarbeiter-Cache: Nummer → Id (+ Liste für Name/Geb-Fallback).
        var employees = await _db.Employees
            .Select(e => new { e.Id, e.EmployeeNumber, e.FirstName, e.LastName, e.DateOfBirth })
            .ToListAsync();
        var byNumber = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in employees)
            if (!string.IsNullOrWhiteSpace(e.EmployeeNumber) && !byNumber.ContainsKey(e.EmployeeNumber))
                byNumber[e.EmployeeNumber] = e.Id;

        // Walter-Vorgabe 06.06.2026: Match-Priorität ZUERST per d.velop-XG-ID,
        // dann Fallback per Dateiname / Beschreibung. Die XG-ID ist die einzig
        // eindeutige Identifikation — d.velop erlaubt mehrere Dokumente mit
        // gleichem Dateinamen, und auch unsere DB kann Dubletten haben. Beim
        // Filename-Match wird die XG-ID auf den Datensatz geschrieben, sodass
        // beim nächsten Run direkt per XG-ID gematched wird (Selbstheilung).
        var allDocs = await _db.EmployeeDokumente.ToListAsync();
        var docByXg     = new Dictionary<string, EmployeeDokument>(StringComparer.OrdinalIgnoreCase);
        var docByFile   = new Dictionary<string, List<EmployeeDokument>>();
        var docByBeschr = new Dictionary<string, List<EmployeeDokument>>();
        foreach (var d in allDocs)
        {
            if (!string.IsNullOrWhiteSpace(d.DvelopDokumentId))
                docByXg[d.DvelopDokumentId.Trim()] = d;
            var fkey = d.EmployeeId + "|" + (d.FilenameOriginal ?? "").Trim().ToLowerInvariant();
            if (!docByFile.TryGetValue(fkey, out var fl)) { fl = new List<EmployeeDokument>(); docByFile[fkey] = fl; }
            fl.Add(d);
            var bem = (d.Bemerkung ?? "").Trim().ToLowerInvariant();
            if (bem.Length > 0)
            {
                var bkey = d.EmployeeId + "|" + bem;
                if (!docByBeschr.TryGetValue(bkey, out var bl)) { bl = new List<EmployeeDokument>(); docByBeschr[bkey] = bl; }
                bl.Add(d);
            }
        }

        // Walter-Vorgabe 06.06.2026: Taxonomie + MA→Filiale für Missing-Doc-Resolve.
        // Damit der „Direkt-Upload"-Button im Frontend ohne weitere Lookups loslegen kann.
        // DokumentKategorie hat keine Typen-Navigation → manueller Join via KategorieId.
        var allKat = await _db.DokumentKategorien.ToListAsync();
        var allTyp = await _db.DokumentTypen.ToListAsync();
        var katById = allKat.ToDictionary(k => k.Id, k => (k.Name ?? "").Trim());
        var typByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in allTyp)
            if (katById.TryGetValue(t.KategorieId, out var kName))
                typByKey[kName + "|" + (t.Name ?? "").Trim()] = t.Id;
        // Primäre Filiale je MA aus Employments — neuester Vertrag (auch inaktive,
        // damit ausgetretene MA wie Nikolina Kajic ihre letzte Filiale behalten).
        // Walter-Vorgabe 06.06.2026: NICHT mehr nur IsActive=true filtern, sonst
        // bleiben Schnell-Uploads für ausgetretene MA blockiert.
        var allEmployments = await _db.Employments
            .Where(e => e.CompanyProfileId != null)
            .Include(e => e.CompanyProfile)
            .Select(e => new { e.EmployeeId, e.ContractStartDate, e.IsActive, BranchCode = e.CompanyProfile!.RestaurantCode ?? "" })
            .ToListAsync();
        var empBranch = allEmployments
            .GroupBy(e => e.EmployeeId)
            .ToDictionary(
                g => g.Key,
                // Aktive Verträge bevorzugen, dann nach Start-Datum absteigend
                g => g.OrderByDescending(e => e.IsActive).ThenByDescending(e => e.ContractStartDate).First().BranchCode
            );

        var result = new BackfillResult { DryRun = dryRun, TotalRows = dataRows.Count };

        foreach (var fields in dataRows)
        {
            string F(int c) => c >= 0 && c < fields.Count ? (fields[c] ?? "").Trim() : "";

            var dateiname = F(colDateiname).Trim('"');
            if (string.IsNullOrEmpty(dateiname)) { result.NoDocument++; continue; }

            // 1) Mitarbeiter auflösen — erst Nummer, dann Name + Geburtsdatum.
            //    Walter-Vorgabe 06.06.2026: Plausibilitäts-Check bei Nummer-Match.
            //    Wenn die Nummer matched, aber Name UND Geburtsdatum widersprechen,
            //    ist die Nummer in d.velop falsch hinterlegt (häufig „1" als Default).
            //    Wenn Name oder Geburtsdatum bestätigt, gewinnt die Nummer trotzdem
            //    — typischer Fall: typografische Abweichung (Emanuel-Iancu vs
            //    Emanuel-Lancu) bei identischem Geburtsdatum.
            int? empId = null;
            var num = F(colMaNummer).Trim('"');
            if (!string.IsNullOrEmpty(num) && byNumber.TryGetValue(num, out var idByNum))
            {
                var vorCheck  = F(colVorname);
                var nachCheck = F(colNachname);
                var gebCheck  = ParseDate(F(colGebDatum));
                if (gebCheck is null)
                {
                    var combined = F(colMaCombined);
                    var slash = combined.LastIndexOf('/');
                    if (slash > 0) gebCheck = ParseDate(combined[(slash + 1)..]);
                }
                var empByNum  = employees.FirstOrDefault(e => e.Id == idByNum);
                bool nameMatches = empByNum != null
                    && (string.IsNullOrWhiteSpace(vorCheck)  || string.Equals(empByNum.FirstName, vorCheck,  StringComparison.OrdinalIgnoreCase))
                    && (string.IsNullOrWhiteSpace(nachCheck) || string.Equals(empByNum.LastName,  nachCheck, StringComparison.OrdinalIgnoreCase));
                bool gebMatches = empByNum?.DateOfBirth.HasValue == true
                    && gebCheck.HasValue
                    && DateOnly.FromDateTime(empByNum.DateOfBirth.Value) == gebCheck.Value;
                // Akzeptieren wenn: keine Konflikt-Hinweise (Name- ODER Geb-Match)
                //                   ODER keine Vergleichsinformation vorhanden
                bool hasNameInfo = !string.IsNullOrWhiteSpace(vorCheck) || !string.IsNullOrWhiteSpace(nachCheck);
                bool hasGebInfo  = gebCheck.HasValue;
                if (!hasNameInfo && !hasGebInfo)       empId = idByNum;
                else if (nameMatches || gebMatches)    empId = idByNum;
                // sonst: empId bleibt null → Name+Geb-Fallback unten
            }
            if (empId is null)
            {
                var vor = F(colVorname); var nach = F(colNachname);
                DateOnly? geb = ParseDate(F(colGebDatum));
                if (string.IsNullOrEmpty(vor) || string.IsNullOrEmpty(nach) || geb is null)
                {
                    var combined = F(colMaCombined);   // "Aleksandra Tomova / 1986-09-11"
                    var slash = combined.LastIndexOf('/');
                    if (slash > 0)
                    {
                        geb ??= ParseDate(combined[(slash + 1)..]);
                        var parts = combined[..slash].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                        {
                            if (string.IsNullOrEmpty(vor))  vor  = parts[0];
                            if (string.IsNullOrEmpty(nach)) nach = string.Join(' ', parts.Skip(1));
                        }
                    }
                }
                if (!string.IsNullOrEmpty(vor) && !string.IsNullOrEmpty(nach))
                {
                    var cand = employees.FirstOrDefault(e =>
                        string.Equals(e.FirstName, vor, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(e.LastName,  nach, StringComparison.OrdinalIgnoreCase) &&
                        (geb is null || (e.DateOfBirth.HasValue && DateOnly.FromDateTime(e.DateOfBirth.Value) == geb)));
                    if (cand != null) empId = cand.Id;
                }
            }
            if (empId is null)
            {
                result.NoEmployee++;
                if (result.Unmatched.Count < 50) result.Unmatched.Add($"Kein MA gefunden: {num} / {dateiname}");
                // Walter-Vorgabe 06.06.2026: strukturierte Missing-MA-Liste — pro
                // eindeutigem MA (Nr + Vor + Nach) Sammeleintrag mit Dokument-Count.
                // Frontend bietet pro MA „Anlegen (Personaldossier)"-Button an.
                var vorRaw  = F(colVorname);
                var nachRaw = F(colNachname);
                if (string.IsNullOrWhiteSpace(vorRaw) || string.IsNullOrWhiteSpace(nachRaw))
                {
                    var combined = F(colMaCombined);
                    var slash = combined.LastIndexOf('/');
                    if (slash > 0)
                    {
                        var parts = combined[..slash].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                        {
                            if (string.IsNullOrWhiteSpace(vorRaw))  vorRaw  = parts[0];
                            if (string.IsNullOrWhiteSpace(nachRaw)) nachRaw = string.Join(' ', parts.Skip(1));
                        }
                    }
                }
                var maKey = (num ?? "") + "|" + (vorRaw ?? "").ToLowerInvariant() + "|" + (nachRaw ?? "").ToLowerInvariant();
                var existing = result.MissingEmployees.FirstOrDefault(m =>
                       string.Equals(m.MaNr, num, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(m.Vorname, vorRaw, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(m.Nachname, nachRaw, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    existing.DokumentCount++;
                }
                else if (result.MissingEmployees.Count < 200)
                {
                    var gebRaw = F(colGebDatum);
                    string gebIso = "";
                    var gebD = ParseDate(gebRaw);
                    if (gebD is null)
                    {
                        // aus Combined holen
                        var combined = F(colMaCombined);
                        var slash = combined.LastIndexOf('/');
                        if (slash > 0) gebD = ParseDate(combined[(slash + 1)..]);
                    }
                    if (gebD.HasValue) gebIso = gebD.Value.ToString("yyyy-MM-dd");

                    var mandant = F(colMandant_BF);
                    var mandantNum = Regex.Match(mandant, @"^\s*(\d+)").Groups[1].Value;
                    var branchCode = !string.IsNullOrEmpty(mandantNum) ? mandantNum.PadLeft(3, '0') : "";

                    result.MissingEmployees.Add(new MissingEmployee
                    {
                        MaNr           = num ?? "",
                        Vorname        = vorRaw ?? "",
                        Nachname       = nachRaw ?? "",
                        GeburtsIso     = gebIso,
                        Mandant        = mandant,
                        BranchCode     = branchCode,
                        DvelopStatus   = F(colMaStatus),
                        DokumentCount  = 1
                    });
                }
                continue;
            }

            // 2) Dokument matchen — Priorität (Walter 06.06.2026):
            //    a) per d.velop-XG-ID (eindeutig, sicher)
            //    b) per (EmpId + Filename) — wenn EINDEUTIG ein DB-Doc übrigbleibt
            //       (also: nur 1 DB-Doc mit dem Namen, das noch keine XG-ID hat
            //       oder dessen XG-ID = unsere ist). XG-ID wird beim Match
            //       gesetzt → künftige Runs treffen direkt Pfad (a).
            //    c) per (EmpId + Beschreibung) — letzter Fallback bei abweichenden Dateinamen
            EmployeeDokument? doc = null;
            var dvelopXg = F(colDokId).Trim();
            if (dvelopXg.Length > 0) docByXg.TryGetValue(dvelopXg, out doc);

            var ekey = empId.Value + "|";
            if (doc == null && docByFile.TryGetValue(ekey + dateiname.ToLowerInvariant(), out var fileList))
            {
                // Kandidaten: Docs ohne XG-ID oder mit passender XG-ID. Docs, die
                // bereits einem ANDEREN d.velop-Eintrag zugewiesen sind, fallen raus.
                var candidates = fileList.Where(d => string.IsNullOrEmpty(d.DvelopDokumentId)
                                                  || string.Equals(d.DvelopDokumentId, dvelopXg, StringComparison.OrdinalIgnoreCase)).ToList();
                if (candidates.Count == 1)
                {
                    doc = candidates[0];
                }
                else if (candidates.Count > 1)
                {
                    // DB-Duplikate (selber Filename, selber MA, mehrere Docs) — meist
                    // d.velop-Versions-Migrations-Artefakte. Disambiguieren über die
                    // Bemerkung (== d.velop "Beschreibung Dokument"); sonst nimm den
                    // ersten. Wichtig: einmal gepickt, kriegt er die XG-ID → künftig
                    // direkt per XG matched, das andere DB-Doc bleibt orphan-bemerkt.
                    var beschr = F(colBeschr).Trim().ToLowerInvariant();
                    var beschrMatch = candidates.FirstOrDefault(d =>
                        !string.IsNullOrWhiteSpace(d.Bemerkung)
                        && string.Equals(d.Bemerkung.Trim().ToLowerInvariant(), beschr, StringComparison.OrdinalIgnoreCase));
                    doc = beschrMatch ?? candidates[0];
                }
                if (doc != null && dvelopXg.Length > 0 && string.IsNullOrEmpty(doc.DvelopDokumentId))
                {
                    doc.DvelopDokumentId = dvelopXg;
                    docByXg[dvelopXg] = doc;  // Map-Update für spätere Zeilen im selben Run
                }
            }
            if (doc == null)
            {
                var beschr = F(colBeschr).Trim().ToLowerInvariant();
                if (beschr.Length > 0 && docByBeschr.TryGetValue(ekey + beschr, out var beschrList))
                {
                    var cands = beschrList.Where(d => string.IsNullOrEmpty(d.DvelopDokumentId)
                                                  || string.Equals(d.DvelopDokumentId, dvelopXg, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (cands.Count == 1)
                    {
                        doc = cands[0];
                        if (dvelopXg.Length > 0 && string.IsNullOrEmpty(doc.DvelopDokumentId))
                        {
                            doc.DvelopDokumentId = dvelopXg;
                            docByXg[dvelopXg] = doc;
                        }
                    }
                }
            }
            if (doc == null)
            {
                result.NoDocument++;
                if (result.Unmatched.Count < 50) result.Unmatched.Add($"Kein Dokument: {num} / {dateiname}");
                    // Walter-Vorgabe 06.06.2026: strukturierte Missing-List, damit
                    // er die fehlenden Dokumente gezielt nachimportieren kann.
                    // Cap bei 1000 Einträgen — bei mehr ist die UI eh überfordert.
                    if (result.MissingDocuments.Count < 1000)
                    {
                        var emp = employees.FirstOrDefault(e => e.Id == empId.Value);
                        var maName = emp != null ? $"{emp.FirstName} {emp.LastName}".Trim() : "";
                        // Typ-Spalte je nach Kategorie auswählen — dynamisch über
                        // die Header-Map (Walter 06.06.2026).
                        var kategorie = F(colKategorie).Replace("HR:", "").Trim();
                        var typ = typColByKategorie.TryGetValue(kategorie, out var typColIdx)
                            ? F(typColIdx) : "";
                        // Backend-Resolves für den Schnell-Upload (Walter 06.06.2026):
                        // Typ-Auflösung mit Plural/Singular-Toleranz (z.B. d.velop
                        // schreibt „Aufenthaltsbewilligungen", wir haben „Aufenthalts-
                        // bewilligung"). Erst Dict-Lookup (exakt), sonst MatchTyp.
                        int? typId = typByKey.TryGetValue(kategorie + "|" + typ, out var tid) ? tid : null;
                        if (typId is null && !string.IsNullOrWhiteSpace(typ))
                        {
                            var katObj = allKat.FirstOrDefault(k => string.Equals(k.Name, kategorie, StringComparison.OrdinalIgnoreCase));
                            if (katObj != null)
                            {
                                var matched = MatchTyp(typ, katObj.Id, allTyp);
                                if (matched != null) typId = matched.Id;
                            }
                        }
                        // BranchCode: zuerst aus Employments (auch inaktive), sonst
                        // aus der Excel-Mandant-Spalte derselben Zeile ableiten.
                        empBranch.TryGetValue(empId.Value, out var branchCode);
                        if (string.IsNullOrWhiteSpace(branchCode))
                        {
                            var rowMandant = F(colMandant_BF);
                            var rowMandantNum = Regex.Match(rowMandant, @"^\s*(\d+)").Groups[1].Value;
                            if (!string.IsNullOrEmpty(rowMandantNum)) branchCode = rowMandantNum.PadLeft(3, '0');
                        }
                        // d.velop-Datumsfelder als ISO-Strings mitgeben, damit der Schnell-
                        // Upload sie am neuen Dokument setzen kann (Walter 06.06.2026).
                        string IsoOrNull(DateTime? d) => d?.ToString("yyyy-MM-ddTHH:mm:ss");
                        result.MissingDocuments.Add(new MissingDocument
                        {
                            MaNr             = num,
                            MaName           = maName,
                            Filename         = dateiname,
                            Kategorie        = kategorie,
                            Typ              = typ,
                            Beschreibung     = F(colBeschr),
                            DokumentId       = F(colDokId),
                            Url              = F(colUrl),
                            EmployeeId       = empId.Value,
                            DokumentTypId    = typId,
                            BranchCode       = branchCode ?? "",
                            ErstelltAm       = IsoOrNull(ParseDateTime(F(colErstelltAm))),
                            GeaendertAm      = IsoOrNull(ParseDateTime(F(colGeaendertAm))),
                            DateiGeaendertAm = IsoOrNull(ParseDateTime(F(colDateiGeaendert))),
                            ZugriffAm        = IsoOrNull(ParseDateTime(F(colZugriffAm))),
                            GeaendertVon     = F(colBesitzer)
                        });
                    }
                continue;
            }

            // 3) Metadaten setzen (nur was vorhanden ist).
            var erstellt       = ParseDateTime(F(colErstelltAm));
            var geaendert      = ParseDateTime(F(colGeaendertAm));
            var dateiGeaendert = ParseDateTime(F(colDateiGeaendert));
            var zugriff        = ParseDateTime(F(colZugriffAm));
            var besitzer       = F(colBesitzer);

            bool any = erstellt.HasValue || geaendert.HasValue || dateiGeaendert.HasValue
                       || zugriff.HasValue || !string.IsNullOrWhiteSpace(besitzer);
            if (!any) { result.NoData++; continue; }

            // Walter-Vorgabe 06.06.2026: ECHTER Diff — nur als „Updated" zählen,
            // wenn mindestens ein Feld vom aktuellen DB-Wert abweicht. Sonst
            // bleibt der Counter beim Re-Import nicht auf der ursprünglichen
            // Zahl hängen. Geänderte Felder werden für die UI-Liste mit-erfasst.
            //
            // Toleranz: ±1 Sek., damit ein Sub-Sekunden-Drift zwischen NPOI-
            // Excel-Parse (Sekunden-Präzision) und Postgres-Roundtrip nicht
            // jedes Mal als „diff" wertet. Die echten d.velop-Werte sind ohnehin
            // sekundengenau (z.B. „22.12.2025 08:38:05"), eine Sekunde Differenz
            // ist also fast immer nur ein Roundtrip-Effekt.
            static bool DateDiff(DateTime? a, DateTime? b)
            {
                if (!a.HasValue) return false;          // CSV-Wert leer → kein Diff
                if (!b.HasValue) return true;            // DB-Wert leer  → echter Diff
                return Math.Abs((a.Value - b.Value).TotalSeconds) > 1.0;
            }
            var changedFields = new List<string>();
            if (DateDiff(erstellt,       doc.ErstelltAm))       changedFields.Add("Erstellt");
            if (DateDiff(geaendert,      doc.GeaendertAm))      changedFields.Add("Geändert");
            if (DateDiff(dateiGeaendert, doc.DateiGeaendertAm)) changedFields.Add("Datei geändert");
            if (DateDiff(zugriff,        doc.ZugriffAm))        changedFields.Add("Geöffnet");
            if (!string.IsNullOrWhiteSpace(besitzer)
                && !string.Equals(besitzer.Trim(), doc.GeaendertVon?.Trim(), StringComparison.Ordinal)) changedFields.Add("Im Besitz von");

            if (changedFields.Count == 0) { result.Unchanged++; continue; }

            if (!dryRun)
            {
                if (erstellt.HasValue)       doc.ErstelltAm       = erstellt;
                if (geaendert.HasValue)      doc.GeaendertAm      = geaendert;
                if (dateiGeaendert.HasValue) doc.DateiGeaendertAm = dateiGeaendert;
                if (zugriff.HasValue)        doc.ZugriffAm        = zugriff;
                if (!string.IsNullOrWhiteSpace(besitzer)) doc.GeaendertVon = besitzer.Trim();
            }
            result.Updated++;

            // Strukturierter UI-Eintrag (Walter 06.06.2026): MA-Nr/-Name +
            // Dateiname + geänderte Felder. Cap bei 1000.
            if (result.UpdatedItems.Count < 1000)
            {
                var empU = employees.FirstOrDefault(e => e.Id == empId.Value);
                var maNameU = empU != null ? $"{empU.FirstName} {empU.LastName}".Trim() : "";
                result.UpdatedItems.Add(new UpdatedItem
                {
                    MaNr          = num,
                    MaName        = maNameU,
                    Filename      = dateiname,
                    Beschreibung  = F(colBeschr),
                    ChangedFields = changedFields
                });
            }
        }

        if (!dryRun) await _db.SaveChangesAsync();
        return Ok(result);
    }

    // ──────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Liest die Metadaten-Datei (CSV oder XLSX) und liefert (Header-Zeile,
    /// Daten-Zeilen). Format wird via Datei-Endung erkannt — .xlsx geht
    /// durch NPOI-XSSFWorkbook (erstes Sheet, idR. „ExcelExport"), alles
    /// andere wird als CSV mit Semikolon-Separator gelesen (UTF-8).
    /// </summary>
    private static (List<string> Headers, List<List<string>> Rows) ReadMetadataFile(IFormFile file)
    {
        var name = (file.FileName ?? "").ToLowerInvariant();
        if (name.EndsWith(".xlsx") || name.EndsWith(".xlsm"))
            return ReadXlsx(file);
        return ReadCsv(file);
    }

    private static (List<string> Headers, List<List<string>> Rows) ReadCsv(IFormFile file)
    {
        using var sr = new StreamReader(file.OpenReadStream(), Encoding.UTF8);
        var lines = new List<string>();
        string? cl;
        while ((cl = sr.ReadLine()) != null) lines.Add(cl);

        var headers = lines.Count > 0 ? ParseCsvLine(lines[0], ';') : new List<string>();
        var rows    = new List<List<string>>();
        for (int i = 1; i < lines.Count; i++)
            rows.Add(ParseCsvLine(lines[i], ';'));
        return (headers, rows);
    }

    private static (List<string> Headers, List<List<string>> Rows) ReadXlsx(IFormFile file)
    {
        using var stream = file.OpenReadStream();
        using var wb = new XSSFWorkbook(stream);
        // Bevorzugt das „ExcelExport"-Sheet (d.velop-Export-Default), sonst Sheet 0.
        ISheet? sheet = null;
        for (int s = 0; s < wb.NumberOfSheets; s++)
        {
            var sh = wb.GetSheetAt(s);
            if (string.Equals(sh.SheetName, "ExcelExport", StringComparison.OrdinalIgnoreCase))
            { sheet = sh; break; }
        }
        sheet ??= wb.GetSheetAt(0);
        if (sheet == null) return (new List<string>(), new List<List<string>>());

        var headerRow = sheet.GetRow(0);
        var headers = new List<string>();
        if (headerRow != null)
        {
            for (int c = 0; c < headerRow.LastCellNum; c++)
                headers.Add((headerRow.GetCell(c)?.ToString() ?? "").Trim());
        }

        var rows = new List<List<string>>();
        for (int r = 1; r <= sheet.LastRowNum; r++)
        {
            var row = sheet.GetRow(r);
            if (row == null) { rows.Add(new List<string>()); continue; }
            var fields = new List<string>(headers.Count);
            for (int c = 0; c < headers.Count; c++)
            {
                var cell = row.GetCell(c);
                fields.Add(CellToString(cell));
            }
            // Komplett-leere Zeile überspringen (XLSX hat oft Trail-Empty-Rows)
            if (fields.All(string.IsNullOrWhiteSpace)) continue;
            rows.Add(fields);
        }
        return (headers, rows);
    }

    /// <summary>
    /// Robustes Cell-zu-String — bei Datums-Cells nutzen wir das ISO-Format
    /// damit ParseDate() die Werte ohne Locale-Tricks erkennt. Numerische
    /// Cells werden ohne Tausender-Trenner ausgegeben.
    /// </summary>
    private static string CellToString(ICell? cell)
    {
        if (cell == null) return "";
        return cell.CellType switch
        {
            CellType.String  => cell.StringCellValue ?? "",
            CellType.Boolean => cell.BooleanCellValue ? "true" : "false",
            CellType.Numeric => DateUtil.IsCellDateFormatted(cell)
                                  ? (cell.DateCellValue?.ToString("yyyy-MM-dd HH:mm:ss") ?? "")
                                  : cell.NumericCellValue.ToString(CultureInfo.InvariantCulture),
            CellType.Formula => cell.CachedFormulaResultType == CellType.Numeric
                                  ? cell.NumericCellValue.ToString(CultureInfo.InvariantCulture)
                                  : (cell.StringCellValue ?? ""),
            CellType.Blank   => "",
            _ => cell.ToString() ?? ""
        };
    }

    private static List<string> ParseCsvLine(string line, char sep)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') inQuotes = false;
                else sb.Append(c);
            }
            else
            {
                if (c == sep) { result.Add(sb.ToString()); sb.Clear(); }
                else if (c == '"') inQuotes = true;
                else sb.Append(c);
            }
        }
        result.Add(sb.ToString());
        return result;
    }

    private static DateOnly? ParseDate(string s)
    {
        s = s.Trim('"', ' ');
        if (string.IsNullOrEmpty(s)) return null;
        // ISO 1995-01-01 oder mit Zeit
        if (DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d1)) return d1;
        // CH 01.01.1995
        if (DateOnly.TryParseExact(s, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d2)) return d2;
        // CH mit Zeit "16.02.2026 09:12:51"
        if (DateTime.TryParseExact(s, new[] { "dd.MM.yyyy HH:mm:ss", "dd.MM.yyyy H:mm:ss", "yyyy-MM-dd HH:mm:ss" },
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt1)) return DateOnly.FromDateTime(dt1);
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return DateOnly.FromDateTime(dt);
        return null;
    }

    /// <summary>
    /// Wie ParseDate, liefert aber den vollen Zeitstempel (mit Uhrzeit) als
    /// DateTime mit Kind=Unspecified → passt auf 'timestamp without time zone'.
    /// Für die Dokument-Metadaten (Erstellt/Geändert/Datei geändert/Zugriff).
    /// </summary>
    private static DateTime? ParseDateTime(string s)
    {
        s = (s ?? "").Trim('"', ' ');
        if (string.IsNullOrEmpty(s)) return null;
        string[] formats = {
            "dd.MM.yyyy HH:mm:ss", "dd.MM.yyyy H:mm:ss",
            "dd.MM.yyyy HH:mm",    "dd.MM.yyyy",
            "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd"
        };
        if (DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt2))
            return DateTime.SpecifyKind(dt2, DateTimeKind.Unspecified);
        return null;
    }

    /// <summary>Match Kategorie via Name (toleriert Whitespace + Case).</summary>
    private static DokumentKategorie? MatchKategorie(string name, List<DokumentKategorie> all)
    {
        name = name.Trim();
        return all.FirstOrDefault(k => string.Equals(k.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Match Typ-Name innerhalb einer Kategorie. Toleriert Plural/Singular.
    /// "Aufenthaltsbewilligungen" matcht "Aufenthaltsbewilligung".
    /// </summary>
    private static DokumentTyp? MatchTyp(string name, int kategorieId, List<DokumentTyp> all)
    {
        name = name.Trim();
        if (string.IsNullOrEmpty(name)) return null;
        var nameLower = name.ToLowerInvariant();

        var inKat = all.Where(t => t.KategorieId == kategorieId).ToList();

        // Direkter Match
        var exact = inKat.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        // Plural-Toleranz: DB "Aufenthaltsbewilligung" matcht File "Aufenthaltsbewilligungen"
        var plural = inKat.FirstOrDefault(t => string.Equals(t.Name + "en", name, StringComparison.OrdinalIgnoreCase)
                                            || string.Equals(t.Name + "n",  name, StringComparison.OrdinalIgnoreCase));
        if (plural != null) return plural;

        // Singular-Toleranz: DB "Bewilligungen" matcht File "Bewilligung"
        var singular = inKat.FirstOrDefault(t =>
            (t.Name.EndsWith("en", StringComparison.OrdinalIgnoreCase) && string.Equals(t.Name[..^2], name, StringComparison.OrdinalIgnoreCase)) ||
            (t.Name.EndsWith("n",  StringComparison.OrdinalIgnoreCase) && string.Equals(t.Name[..^1], name, StringComparison.OrdinalIgnoreCase)));
        return singular;
    }

    private int? GetCurrentUserId()
    {
        var sub = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(sub, out var id) ? id : null;
    }
}
