using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NPOI.HSSF.UserModel;
using NPOI.XSSF.UserModel;
using NPOI.SS.UserModel;
using System.Text.RegularExpressions;

namespace HrSystem.Controllers;

/// <summary>
/// Bulk-Import von Bankverbindungen aus Mirus-Lohnabrechnungs-XLS.
///
/// Mirus-Layout: pro Mitarbeiter ein Block von ~37 Zeilen, beginnend mit
/// "Herr"/"Frau" in Spalte B. Direkt darunter der Vor-/Nachname. Gegen
/// Block-Ende eine "Zahlung am: TT.MM.JJJJ (DTA / ISO 20022) BANK (CHxxx…)"-
/// Zeile, woraus die IBAN extrahiert wird.
///
/// Strategie:
/// 1. POST /api/imports/bank/preview  →  XLS parsen, MA matchen,
///    pro IBAN den BankLookupService konsultieren, Vorschau zurückgeben.
/// 2. POST /api/imports/bank/commit   →  approved Zeilen in
///    employee_bank_account einfügen (idempotent).
///
/// Bank-Name kommt NICHT aus dem Mirus-String (oft inkonsistent), sondern
/// aus der gepflegten BankMaster-Tabelle via IID-Lookup. Lohnabtretungen
/// (Mirus-String enthält ":") werden erkannt und übersprungen.
/// </summary>
[Authorize(Roles = "admin,superuser")]
[ApiController]
[Route("api/imports/bank")]
public class BankImportController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly BankLookupService _bankSvc;
    // Schweizer IBAN: exakt 21 Zeichen (CH + 2 Prüfziffern + 17 BBAN-Ziffern).
    // Strikte Länge — bei truncierten Werten lieber INVALID_IBAN melden als
    // einen falschen 20-Zeichen-Wert importieren (ist beim Mirus-Import schon
    // einmal passiert weil die XLS die IBAN über 2 Zellen verteilt hatte).
    //
    // CH-IBAN = "CH" + 2 Prüfziffern + 17 weitere Stellen. PostFinance,
    // Raiffeisen, Kantonalbanken usw. = rein numerisch. UBS verwendet
    // alphanumerische Endungen (z.B. CH87…140K, CH33…740V, CH84…740P) —
    // ISO 13616 lässt BBAN alphanumerisch zu (Walter-Feedback 13.05.2026:
    // diese IBANs liessen sich manuell beim MA eintragen, der Import-Parser
    // hat sie aber als „IBAN-Format nicht erkannt" abgelehnt).
    private static readonly Regex IbanRegex = new(@"CH\d{2}[A-Z0-9]{17}(?![A-Z0-9])", RegexOptions.Compiled);
    private static readonly Regex BankInLineRegex = new(@"ISO 20022\)\s*([^(]+?)\s*\(CH", RegexOptions.Compiled);

    public BankImportController(AppDbContext db, BankLookupService bankSvc)
    {
        _db = db;
        _bankSvc = bankSvc;
    }

    public record PreviewRow(
        string Name,
        string Iban,
        string? BankName,         // aus BankMaster — kann null sein wenn IID unbekannt
        string? Bic,
        int? EmployeeId,          // null wenn kein Match
        string? EmployeeNumber,
        string Status,            // MATCH | NO_EMPLOYEE | DUPLICATE | LOHNABTRETUNG | INVALID_IBAN | UNKNOWN_BANK
        string? Hint              // optionaler Hinweistext für UI
    );

    public record PreviewResponse(
        int CompanyProfileId,
        string CompanyProfileName,
        int TotalEntries,
        int Importable,
        int Skipped,
        List<PreviewRow> Rows
    );

    /// <summary>
    /// XLS hochladen, Preview generieren. Datei + companyProfileId via
    /// multipart/form-data.
    /// </summary>
    [HttpPost("preview")]
    [RequestSizeLimit(10_000_000)]   // 10 MB
    public async Task<IActionResult> Preview([FromForm] IFormFile file, [FromForm] int companyProfileId)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "Keine Datei hochgeladen." });

        var company = await _db.CompanyProfiles.FindAsync(companyProfileId);
        if (company == null)
            return BadRequest(new { message = "Filiale nicht gefunden." });

        // XLS parsen
        List<(string Name, string Iban, bool IsLohnabtretung, string? MirusBank)> blocks;
        try
        {
            blocks = ParseMirusXls(file);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"XLS konnte nicht gelesen werden: {ex.Message}" });
        }

        // MA der gewählten Filiale laden — für Vor-/Nachname-Matching.
        // Auch INAKTIVE MA + inaktive Employments einschliessen: in der Mirus-
        // Lohnabrechnung können MA stehen, die zwischenzeitlich ausgetreten
        // sind aber im abrechneten Monat noch in der Filiale arbeiteten —
        // deren Bankverbindung soll genauso importierbar sein.
        var employees = await _db.Employees
            .Where(e => e.Employments.Any(emp => emp.CompanyProfileId == companyProfileId))
            .Select(e => new { e.Id, e.FirstName, e.LastName, e.EmployeeNumber })
            .ToListAsync();

        // Existierende Bankverbindungen (für Duplikat-Erkennung)
        var existingBanks = await _db.EmployeeBankAccounts
            .Where(b => employees.Select(e => e.Id).Contains(b.EmployeeId))
            .Select(b => new { b.EmployeeId, b.Iban })
            .ToListAsync();
        var existingSet = new HashSet<string>(
            existingBanks.Select(b => $"{b.EmployeeId}|{NormalizeIban(b.Iban)}"));

        // Pro Block: matchen + Status setzen
        var rows = new List<PreviewRow>();
        foreach (var b in blocks)
        {
            // Lohnabtretung: separat markieren, kein Import
            if (b.IsLohnabtretung)
            {
                rows.Add(new PreviewRow(b.Name, "", null, null, null, null,
                    "LOHNABTRETUNG",
                    "Lohn geht via Lohnabtretung an Behörde — bitte manuell unter Behörden/Pfändung erfassen."));
                continue;
            }

            var ibanClean = NormalizeIban(b.Iban);
            if (string.IsNullOrEmpty(ibanClean) || !ibanClean.StartsWith("CH"))
            {
                rows.Add(new PreviewRow(b.Name, b.Iban, null, null, null, null,
                    "INVALID_IBAN", "IBAN-Format nicht erkannt."));
                continue;
            }

            // BankLookup
            var bankInfo = _bankSvc.LookupByIban(ibanClean);

            // MA-Match: Name "Vorname Nachname" splitten am letzten Leerzeichen.
            // Tolerant in zwei Dimensionen — beide kommen in der Praxis vor:
            //   1) Vor- und Nachname vertauscht (südasiatische Namen, easy@work-
            //      Datenerfassung uneinheitlich).
            //   2) Mirus kürzt lange Namen ab (Spalten-Limit) — z.B. System hat
            //      "Hosseinigharehtakan", Mirus liefert "Hosseinigharehta".
            //      → Prefix-Match (mind. 6 Zeichen) wird akzeptiert mit Hinweis.
            var (vor, nach) = SplitName(b.Name);
            bool nameSwapped   = false;
            bool nameTruncated = false;
            var match = employees.FirstOrDefault(e =>
                NameLike(e.FirstName, vor)  &&
                NameLike(e.LastName,  nach));
            if (match == null)
            {
                // Vertauscht probieren
                match = employees.FirstOrDefault(e =>
                    NameLike(e.FirstName, nach) &&
                    NameLike(e.LastName,  vor));
                if (match != null) nameSwapped = true;
            }
            if (match != null)
            {
                // Wenn irgendeine Komponente nicht exakt war → Hinweis
                bool fnExact = match.FirstName.Equals(nameSwapped ? nach : vor,
                                                     StringComparison.OrdinalIgnoreCase);
                bool lnExact = match.LastName .Equals(nameSwapped ? vor  : nach,
                                                     StringComparison.OrdinalIgnoreCase);
                nameTruncated = !(fnExact && lnExact);
            }

            if (match == null)
            {
                rows.Add(new PreviewRow(b.Name, ibanClean, bankInfo?.Name, bankInfo?.Bic, null, null,
                    "NO_EMPLOYEE",
                    "Kein MA mit diesem Vor-/Nachnamen in der Filiale (auch vertauscht / mit Prefix-Toleranz geprüft)."));
                continue;
            }

            var dupKey = $"{match.Id}|{ibanClean}";
            if (existingSet.Contains(dupKey))
            {
                rows.Add(new PreviewRow(b.Name, ibanClean, bankInfo?.Name, bankInfo?.Bic,
                    match.Id, match.EmployeeNumber, "DUPLICATE",
                    "IBAN ist beim MA bereits hinterlegt."));
                continue;
            }

            if (bankInfo == null)
            {
                rows.Add(new PreviewRow(b.Name, ibanClean, null, null, match.Id, match.EmployeeNumber,
                    "UNKNOWN_BANK",
                    $"Bank-IID nicht in BankMaster gefunden (Mirus-Hinweis: {b.MirusBank ?? "-"})."));
                continue;
            }

            // Hinweise zusammenstellen — wenn Name vertauscht oder Namen-
            // Komponente nicht exakt gleich war (Mirus-Spaltenkürzung). Import
            // läuft normal durch, User soll's nur sehen.
            var hints = new List<string>();
            if (nameSwapped)
                hints.Add("Vor-/Nachname vertauscht");
            if (nameTruncated)
                hints.Add($"Name nicht exakt — Mirus «{b.Name}» vs. System «{match.FirstName} {match.LastName}» (Prefix-Match)");
            string? matchHint = hints.Count > 0
                ? string.Join(" · ", hints) + ". Bitte prüfen."
                : null;

            rows.Add(new PreviewRow(b.Name, ibanClean, bankInfo.Name, bankInfo.Bic,
                match.Id, match.EmployeeNumber, "MATCH", matchHint));
        }

        return Ok(new PreviewResponse(
            CompanyProfileId: companyProfileId,
            CompanyProfileName: company.BranchName ?? company.CompanyName,
            TotalEntries: rows.Count,
            Importable: rows.Count(r => r.Status == "MATCH"),
            Skipped: rows.Count(r => r.Status != "MATCH"),
            Rows: rows
        ));
    }

    public record CommitRequest(
        int CompanyProfileId,
        List<CommitRow> Rows
    );
    public record CommitRow(int EmployeeId, string Iban, string? BankName, string? Bic, string? ValidFrom);

    /// <summary>
    /// Schreibt die approved Bank-Einträge in employee_bank_account. Idempotent
    /// auf IBAN-Ebene pro MA. Nur Zeilen mit Status MATCH sollten übergeben
    /// werden — der Endpoint prüft aber zur Sicherheit nochmal Duplikate.
    /// </summary>
    [HttpPost("commit")]
    public async Task<IActionResult> Commit([FromBody] CommitRequest req)
    {
        if (req.Rows == null || req.Rows.Count == 0)
            return BadRequest(new { message = "Keine Einträge zum Speichern." });

        int created = 0;
        int skipped = 0;
        var details = new List<string>();

        foreach (var row in req.Rows)
        {
            var iban = NormalizeIban(row.Iban);
            if (string.IsNullOrEmpty(iban) || row.EmployeeId <= 0)
            {
                skipped++; continue;
            }

            // Re-Check Duplikat
            var exists = await _db.EmployeeBankAccounts
                .AnyAsync(b => b.EmployeeId == row.EmployeeId && b.Iban == iban);
            if (exists) { skipped++; continue; }

            var validFrom = DateOnly.FromDateTime(DateTime.Today);
            if (DateOnly.TryParse(row.ValidFrom, out var parsed))
                validFrom = parsed;

            _db.EmployeeBankAccounts.Add(new EmployeeBankAccount
            {
                EmployeeId      = row.EmployeeId,
                Iban            = iban,
                Bic             = row.Bic,
                BankName        = row.BankName,
                IsHauptbank     = true,
                AufteilungTyp   = "VOLL",
                ValidFrom       = validFrom,
                CreatedAt       = DateTime.UtcNow,
                UpdatedAt       = DateTime.UtcNow
            });
            created++;
        }

        await _db.SaveChangesAsync();
        return Ok(new { created, skipped });
    }

    // ── XLS-Parser ─────────────────────────────────────────────────────

    /// <summary>
    /// Liest die Mirus-Lohnabrechnungs-XLS und gruppiert in Blöcke pro MA.
    /// Block-Marker: "Herr"/"Frau" in Spalte B. Pro Block: Name (Zeile direkt
    /// nach Anrede) + IBAN aus "Zahlung am:..."-Zeile.
    /// </summary>
    private static List<(string Name, string Iban, bool IsLohnabtretung, string? MirusBank)>
        ParseMirusXls(IFormFile file)
    {
        using var stream = file.OpenReadStream();
        IWorkbook wb;
        try
        {
            // Mirus exportiert .xls (alt-binary). Falls Walter mal .xlsx
            // hochlädt, muss XSSFWorkbook genommen werden.
            wb = file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
                ? new XSSFWorkbook(stream)
                : new HSSFWorkbook(stream);
        }
        catch
        {
            stream.Position = 0;
            // Fallback: andere Format-Variante probieren
            wb = new XSSFWorkbook(stream);
        }

        var sheet = wb.GetSheetAt(0);

        // ── Format-Auto-Detect ───────────────────────────────────────────
        // Mirus liefert ZWEI verschiedene XLS-Formate:
        //   1. „Lohnabrechnung" — Block pro MA (~37 Zeilen, Anrede + Name +
        //      Adresse + Lohnpositionen + „Zahlung am: …"-Zeile mit IBAN).
        //   2. „Zahlungen_X_YYYY" — kompakte Tabelle mit Header
        //      „Name | Betrag | BCNr. | IBAN / Konto-Nr." und einer Zeile
        //      pro MA. Lohnabtretung erkennt man am „/" im Namen
        //      (z.B. „Stoianova Halyna / ORS Service AG").
        // Wir prüfen die ersten 10 Zeilen auf den Tabellen-Header und
        // wählen den passenden Parser.
        bool isZahlungenFormat = false;
        for (int r = 0; r <= Math.Min(10, sheet.LastRowNum); r++)
        {
            var row = sheet.GetRow(r);
            if (row == null) continue;
            for (int c = 0; c < row.LastCellNum; c++)
            {
                var v = row.GetCell(c)?.ToString()?.Trim() ?? "";
                if (v.Equals("IBAN / Konto-Nr.", StringComparison.OrdinalIgnoreCase)
                 || v.Equals("IBAN/Konto-Nr.", StringComparison.OrdinalIgnoreCase))
                { isZahlungenFormat = true; break; }
            }
            if (isZahlungenFormat) break;
        }

        if (isZahlungenFormat)
            return ParseZahlungenXls(sheet);

        var rows  = new List<(string Name, string Iban, bool IsLohnabtretung, string? MirusBank)>();

        // Aktueller Block-Zustand
        string? currentName = null;
        bool nameJustSet = false;

        for (int r = sheet.FirstRowNum; r <= sheet.LastRowNum; r++)
        {
            var row = sheet.GetRow(r);
            if (row == null) continue;
            var cell = row.GetCell(1);   // Spalte B
            if (cell == null) continue;
            var v = (cell.CellType == CellType.String ? cell.StringCellValue : cell.ToString())?.Trim() ?? "";
            if (v.Length == 0) continue;

            if (v == "Herr" || v == "Frau")
            {
                currentName = null;
                nameJustSet = false;
                continue;
            }

            if (currentName == null && !v.StartsWith("c/o") && !v.StartsWith("Postfach")
                && !v.Contains("Zahlung am") && !char.IsDigit(v[0]))
            {
                // Nächste Zeile nach Anrede ohne PLZ/Strasse-Marker = Name
                currentName = v;
                nameJustSet = true;
                continue;
            }

            if (currentName != null && v.Contains("Zahlung am") && v.Contains("CH"))
            {
                // IBAN kann in der Mirus-XLS über mehrere Spalten verteilt sein
                // (z.B. Spalte B endet mit "...0540", Spalte C beginnt mit "0)").
                // Daher die ganze Zeile zusammenführen, bevor wir matchen.
                var combined = v;
                var lastCol = (int)row.LastCellNum;
                for (int c = 2; c < lastCol; c++)
                {
                    var cl = row.GetCell(c);
                    if (cl == null) continue;
                    var s = (cl.CellType == CellType.String ? cl.StringCellValue : cl.ToString())?.Trim() ?? "";
                    if (s.Length > 0) combined += " " + s;
                }
                // Whitespace inkl. NBSP entfernen vor IBAN-Match
                var combinedNoWs = Regex.Replace(combined, @"\s", "");
                var ibanMatch = IbanRegex.Match(combinedNoWs);
                var bankMatch = BankInLineRegex.Match(combined);
                var bank = bankMatch.Success ? bankMatch.Groups[1].Value.Trim() : null;
                var isLohnabtretung = bank != null && bank.Contains(":");

                rows.Add((
                    Name: currentName,
                    Iban: ibanMatch.Success ? ibanMatch.Value : "",
                    IsLohnabtretung: isLohnabtretung,
                    MirusBank: isLohnabtretung ? null : bank
                ));
                currentName = null;
                nameJustSet = false;
            }
        }

        // MA OHNE "Zahlung am"-Zeile (z.B. weil 100% Lohnabtretung) auch listen
        // — nur dann wenn currentName noch gesetzt ist und eine NEUE Anrede kam.
        // Aktuell trickier weil unser Parser Block-orientiert ist. Wir
        // begnügen uns mit den gefundenen Zahl-Einträgen — fehlende MA
        // bedeuten "keine Bank in Mirus, manuell prüfen".

        return rows;
    }

    /// <summary>
    /// Parser für das Mirus „Zahlungen_X_YYYY"-Format. Tabellen-Layout mit
    /// Header-Zeile „Name | Betrag | BCNr. | IBAN / Konto-Nr." und pro MA
    /// genau einer Zeile.
    ///
    /// Lohnabtretungen erkennt man am „/" im Namen
    /// (z.B. „Stoianova Halyna / ORS Service AG") — diese werden als
    /// IsLohnabtretung=true gemeldet und beim Commit übersprungen.
    ///
    /// Total-Zeile (Name=„Total" oder leerer IBAN) wird ignoriert.
    /// </summary>
    private static List<(string Name, string Iban, bool IsLohnabtretung, string? MirusBank)>
        ParseZahlungenXls(ISheet sheet)
    {
        var rows = new List<(string Name, string Iban, bool IsLohnabtretung, string? MirusBank)>();

        // Header finden (irgendwo in den ersten 10 Zeilen). Wir merken uns
        // die Spalten-Indexe für Name + IBAN.
        int headerRow = -1, colName = -1, colIban = -1;
        for (int r = 0; r <= Math.Min(10, sheet.LastRowNum); r++)
        {
            var row = sheet.GetRow(r);
            if (row == null) continue;
            for (int c = 0; c < row.LastCellNum; c++)
            {
                var v = row.GetCell(c)?.ToString()?.Trim() ?? "";
                if (v.Equals("Name", StringComparison.OrdinalIgnoreCase)) colName = c;
                if (v.StartsWith("IBAN", StringComparison.OrdinalIgnoreCase)) colIban = c;
            }
            if (colName >= 0 && colIban >= 0) { headerRow = r; break; }
        }
        if (headerRow < 0) return rows;

        for (int r = headerRow + 1; r <= sheet.LastRowNum; r++)
        {
            var row = sheet.GetRow(r);
            if (row == null) continue;
            var name = row.GetCell(colName)?.ToString()?.Trim() ?? "";
            var iban = row.GetCell(colIban)?.ToString()?.Trim() ?? "";
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(iban)) continue;
            if (name.Equals("Total", StringComparison.OrdinalIgnoreCase)) continue;

            // Lohnabtretung: „/ <Empfänger>" am Ende. Nur den MA-Namen behalten.
            bool isLohnabtretung = name.Contains('/');
            string cleanName = isLohnabtretung
                ? name.Substring(0, name.IndexOf('/')).Trim()
                : name;

            // IBAN-Whitespace entfernen
            var ibanClean = Regex.Replace(iban, @"\s", "").ToUpperInvariant();

            rows.Add((
                Name: cleanName,
                Iban: ibanClean,
                IsLohnabtretung: isLohnabtretung,
                MirusBank: null    // Bank-Name kommt aus BankMaster via IID
            ));
        }
        return rows;
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static string NormalizeIban(string? iban)
    {
        if (string.IsNullOrWhiteSpace(iban)) return "";
        return new string(iban.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
    }

    /// <summary>
    /// Tolerant Name-Comparison: exakt gleich, oder eine Seite ist Prefix der
    /// anderen (mind. 6 Zeichen) — fängt Mirus-Spaltenkürzung bei langen
    /// Nachnamen ab. Casing wird ignoriert.
    /// </summary>
    private static bool NameLike(string? sysName, string? mirusName)
    {
        if (string.IsNullOrWhiteSpace(sysName) || string.IsNullOrWhiteSpace(mirusName))
            return false;
        var a = sysName.Trim();
        var b = mirusName.Trim();
        if (a.Equals(b, StringComparison.OrdinalIgnoreCase)) return true;
        var shorter = a.Length <= b.Length ? a : b;
        var longer  = a.Length <= b.Length ? b : a;
        const int MIN_PREFIX = 6;
        return shorter.Length >= MIN_PREFIX
            && longer.StartsWith(shorter, StringComparison.OrdinalIgnoreCase);
    }

    private static (string Vorname, string Nachname) SplitName(string fullName)
    {
        var trim = fullName.Trim();
        var lastSpace = trim.LastIndexOf(' ');
        if (lastSpace < 0) return ("", trim);
        return (trim[..lastSpace].Trim(), trim[(lastSpace + 1)..].Trim());
    }
}
