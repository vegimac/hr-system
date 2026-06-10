using System.Globalization;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NPOI.HSSF.UserModel;     // .xls (HSSF)
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;     // .xlsx (XSSF)

namespace HrSystem.Controllers;

/// <summary>
/// Bulk-Import der Eröffnungs-Saldi aus Mirus-Exporten (Walter-Vorgabe 26.05.2026).
///
/// CHF-Import (Endpoint: chf/analyze + chf/commit): liest die Mirus
/// „Rückstellungsliste Saldomethode" (XLS) und befüllt die Vortrag-Felder
/// 905 (Ferien-Geld CHF) + 906 (13. ML CHF) pro MA in EINER Migrations-Periode
/// (typischerweise „2026-01"). Stunden-Saldi (col M in CHF) werden bewusst
/// IGNORIERT — die kommen später aus dem Stunden-Import (Saldiübersicht in
/// Stunden, Spalte 22 → Vortrag-Code 901).
///
/// Lock: bewusst KEINE LohnEditLockService-Prüfung (Walter im Test-Modus,
/// nur admin importiert). Die einzelne Upsert via SaldoVortragController
/// hätte den Lock; hier ist es ein einmaliger Migrations-Lauf in einer
/// offenen Periode.
///
/// Mechanik: pro MA werden 905 + 906 als LohnZulage-Eintrag mit
/// Kategorie="Saldo-Vortrag" angelegt/aktualisiert — die anderen
/// Vortrag-Codes (901-904) bleiben UNANGETASTET (anders als
/// SaldoVortragController.Upsert, der alle 6 Felder auf einmal setzt).
/// Vertragsmodell-Relevanz (Walter-Vorgabe): 905 nur für UTP/MTP,
/// 906 für MTP/FIX/FIX-M (irrelevant für UTP).
/// </summary>
[Authorize(Roles = "admin,superuser")]
[ApiController]
[Route("api/saldo-vortrag-import")]
public class SaldoVortragImportController : ControllerBase
{
    private readonly AppDbContext _db;

    public SaldoVortragImportController(AppDbContext db) { _db = db; }

    // ── DTOs ─────────────────────────────────────────────────────────────────

    public record ParsedRow(
        int     RowNumber,
        string  KStelle,
        string  Name,
        decimal DreizehnterChf,    // col G (idx 6)  — „13. MLohn 100%"
        decimal FerienGeldChf,     // col K (idx 10) — „Ferien" (CHF Saldo)
        decimal StundenChf         // col M (idx 12) — informativ, nicht importiert
    );

    public record BranchEmployee(int Id, string FirstName, string LastName, string? EmployeeNumber, string? EmploymentModel);

    public record AnalyzeRow(
        int     RowNumber,
        string  KStelle,
        string  Name,
        decimal DreizehnterChf,
        decimal FerienGeldChf,
        decimal StundenChf,
        int?    EmployeeId,
        string? EmployeeMatchedName,
        string? EmployeeNumber,
        string? EmploymentModel,
        string  Status   // MATCH / NO_MATCH / AMBIGUOUS
    );

    public record AnalyzeResult(
        string             Periode,
        int                CompanyProfileId,
        int                Total,
        int                Matched,
        int                NoMatch,
        int                Ambiguous,
        List<AnalyzeRow>   Rows,
        List<BranchEmployee> BranchEmployees    // für manuellen Picker
    );

    public record CommitRow(
        int     EmployeeId,
        decimal DreizehnterChf,
        decimal FerienGeldChf,
        string? OriginalName   // nur fürs Bemerkungs-Feld / Audit
    );

    public record CommitDto(
        int             CompanyProfileId,
        string          Periode,
        List<CommitRow> Rows
    );

    public record CommitResult(
        int Created,
        int Updated,
        int Skipped,
        List<string> Hinweise
    );

    // ── ANALYZE ──────────────────────────────────────────────────────────────

    [HttpPost("chf/analyze")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> AnalyzeChf(
        [FromQuery] int    companyProfileId,
        [FromQuery] string periode,
        [FromForm]  IFormFile file)
    {
        if (file is null || file.Length == 0) return BadRequest(new { error = "Datei fehlt." });
        if (string.IsNullOrEmpty(periode) || periode.Length != 7 || periode[4] != '-')
            return BadRequest(new { error = "Periode muss im Format YYYY-MM sein." });

        List<ParsedRow> rows;
        try { rows = ParseSaldomethode(file); }
        catch (Exception ex)
        {
            return BadRequest(new { error = "Datei konnte nicht gelesen werden: " + ex.Message });
        }

        // Branch-MA laden (aktiv, mit Vertrag in dieser Filiale).
        var branchEmps = await _db.Employees
            .Where(e => e.IsActive && e.Employments.Any(em => em.CompanyProfileId == companyProfileId))
            .Select(e => new BranchEmployee(
                e.Id,
                e.FirstName ?? "",
                e.LastName ?? "",
                e.EmployeeNumber,
                e.Employments
                    .Where(em => em.IsActive && em.CompanyProfileId == companyProfileId)
                    .OrderByDescending(em => em.ContractStartDate)
                    .Select(em => em.EmploymentModel)
                    .FirstOrDefault()))
            .ToListAsync();

        var sortedEmps = branchEmps
            .OrderBy(b => b.FirstName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(b => b.LastName,  StringComparer.OrdinalIgnoreCase)
            .ToList();

        var analyzeRows = new List<AnalyzeRow>();
        foreach (var r in rows)
        {
            var candidates = MatchByName(r.Name, branchEmps);
            string  status; int? empId = null; string? matched = null, num = null, model = null;
            if (candidates.Count == 1)
            {
                status  = "MATCH";
                empId   = candidates[0].Id;
                matched = $"{candidates[0].FirstName} {candidates[0].LastName}".Trim();
                num     = candidates[0].EmployeeNumber;
                model   = candidates[0].EmploymentModel;
            }
            else if (candidates.Count == 0) status = "NO_MATCH";
            else                            status = "AMBIGUOUS";

            analyzeRows.Add(new AnalyzeRow(
                r.RowNumber, r.KStelle, r.Name,
                r.DreizehnterChf, r.FerienGeldChf, r.StundenChf,
                empId, matched, num, model, status));
        }

        return Ok(new AnalyzeResult(
            Periode:          periode,
            CompanyProfileId: companyProfileId,
            Total:            analyzeRows.Count,
            Matched:          analyzeRows.Count(x => x.Status == "MATCH"),
            NoMatch:          analyzeRows.Count(x => x.Status == "NO_MATCH"),
            Ambiguous:        analyzeRows.Count(x => x.Status == "AMBIGUOUS"),
            Rows:             analyzeRows,
            BranchEmployees:  sortedEmps));
    }

    // ── COMMIT ───────────────────────────────────────────────────────────────

    [HttpPost("chf/commit")]
    public async Task<IActionResult> CommitChf([FromBody] CommitDto dto)
    {
        if (dto is null) return BadRequest(new { error = "Body fehlt." });
        if (string.IsNullOrEmpty(dto.Periode) || dto.Periode.Length != 7 || dto.Periode[4] != '-')
            return BadRequest(new { error = "Periode ungültig." });
        if (dto.Rows is null || dto.Rows.Count == 0)
            return BadRequest(new { error = "Keine Zeilen zum Speichern." });

        // Vortrag-Lohnpositionen 905 (Ferien-Geld CHF) + 906 (13. ML CHF) laden.
        var lps = await _db.Lohnpositionen
            .Where(l => l.Kategorie == "Saldo-Vortrag" && (l.Code == "905" || l.Code == "906"))
            .ToDictionaryAsync(l => l.Code, l => l);

        if (!lps.ContainsKey("905") || !lps.ContainsKey("906"))
            return Problem("Vortrag-Lohnpositionen 905/906 fehlen. Bitte add_saldo_vortrag.sql ausführen.", statusCode: 500);

        // MA + Vertragsmodelle laden (für Relevanz-Filterung pro MA).
        var empIds = dto.Rows.Select(r => r.EmployeeId).Distinct().ToList();
        var emps = await _db.Employees
            .Include(e => e.Employments)
            .Where(e => empIds.Contains(e.Id))
            .ToListAsync();

        // Bestehende Vortrag-Einträge dieser MA für 905/906 laden (für Upsert).
        var existing = await _db.LohnZulagen
            .Include(z => z.Lohnposition)
            .Where(z => empIds.Contains(z.EmployeeId)
                     && z.Lohnposition!.Kategorie == "Saldo-Vortrag"
                     && (z.Lohnposition.Code == "905" || z.Lohnposition.Code == "906"))
            .ToListAsync();

        int created = 0, updated = 0, skipped = 0;
        var hinweise = new List<string>();

        foreach (var row in dto.Rows)
        {
            var emp = emps.FirstOrDefault(e => e.Id == row.EmployeeId);
            if (emp is null) { skipped++; hinweise.Add($"MA-ID {row.EmployeeId} nicht gefunden — übersprungen ({row.OriginalName})."); continue; }

            var activeEmp = emp.Employments
                .Where(e => e.IsActive)
                .OrderByDescending(e => e.ContractStartDate)
                .FirstOrDefault();
            var model = (activeEmp?.EmploymentModel ?? "").ToUpperInvariant();

            UpsertCode("905", lps["905"], row.FerienGeldChf,  IsRelevant905(model));
            UpsertCode("906", lps["906"], row.DreizehnterChf, IsRelevant906(model));

            void UpsertCode(string code, Lohnposition lp, decimal value, bool relevant)
            {
                var ex = existing.FirstOrDefault(e =>
                    e.EmployeeId == row.EmployeeId && e.Lohnposition!.Code == code);

                if (!relevant)
                {
                    // Pro Vertragsmodell nicht geführt — bestehenden Eintrag entfernen,
                    // damit der Vortrag sauber zum Modell passt (entspricht der Logik
                    // in SaldoVortragController.Upsert).
                    if (ex != null) { _db.LohnZulagen.Remove(ex); }
                    return;
                }

                var betrag = Math.Round(value, 2);
                if (ex == null)
                {
                    _db.LohnZulagen.Add(new LohnZulage
                    {
                        EmployeeId     = row.EmployeeId,
                        Periode        = dto.Periode,
                        LohnpositionId = lp.Id,
                        Betrag         = betrag,
                        Bemerkung      = "Migrations-Vortrag aus Mirus Saldomethode (CHF)",
                        CreatedAt      = DateTime.UtcNow,
                        UpdatedAt      = DateTime.UtcNow
                    });
                    created++;
                }
                else
                {
                    ex.Periode   = dto.Periode;
                    ex.Betrag    = betrag;
                    ex.UpdatedAt = DateTime.UtcNow;
                    updated++;
                }
            }
        }

        await _db.SaveChangesAsync();
        return Ok(new CommitResult(created, updated, skipped, hinweise));
    }

    // ── Vertragsmodell-Relevanz (analog SaldoVortragController) ─────────────

    private static bool IsRelevant905(string model) =>   // Ferien-Geld CHF
        model == "UTP" || model == "MTP";
    private static bool IsRelevant906(string model) =>   // 13. ML CHF
        model == "MTP" || model == "FIX" || model == "FIX-M";

    // ── XLS-Parser ───────────────────────────────────────────────────────────

    private static List<ParsedRow> ParseSaldomethode(IFormFile file)
    {
        using var stream = file.OpenReadStream();
        IWorkbook wb;
        try
        {
            wb = file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
                ? new XSSFWorkbook(stream)
                : new HSSFWorkbook(stream);
        }
        catch
        {
            stream.Position = 0;
            wb = new XSSFWorkbook(stream);
        }

        var sh = wb.GetSheetAt(0);
        var rows = new List<ParsedRow>();
        string currentKStelle = "";

        // Mirus-Layout (beobachtet):
        //   Zeile 0..4 = Header/Adresse
        //   Zeile 5    = Spaltenüberschriften
        //   Zeile 6    = Leerzeile
        //   Zeile 7+   = Daten — entweder Kostenstellen-Header (Col 1 gefüllt,
        //                Col 2 leer) oder Daten-Zeile (Col 2 = Name) oder
        //                Summenzeile (Name beginnt mit „Total ").
        //   Spalten (0-indexed): 1=Kostenstelle-Header | 2=Name | 6=13. ML CHF
        //                        | 10=Ferien (CHF Saldo) | 12=Stunden CHF (info).
        for (int r = 7; r <= sh.LastRowNum; r++)
        {
            var row = sh.GetRow(r);
            if (row is null) continue;

            var kst  = (row.GetCell(1)?.ToString() ?? "").Trim();
            var name = (row.GetCell(2)?.ToString() ?? "").Trim();

            if (!string.IsNullOrEmpty(kst) && string.IsNullOrEmpty(name))
            {
                currentKStelle = kst;
                continue;
            }
            if (string.IsNullOrEmpty(name)) continue;
            if (name.StartsWith("Total ", StringComparison.OrdinalIgnoreCase)) continue;

            var g = ReadDecimal(row.GetCell(6));
            var k = ReadDecimal(row.GetCell(10));
            var m = ReadDecimal(row.GetCell(12));

            rows.Add(new ParsedRow(r + 1, currentKStelle, name, g, k, m));   // r+1 = Excel-1-basiert
        }
        return rows;
    }

    private static decimal ReadDecimal(ICell? cell)
    {
        if (cell is null) return 0;
        try
        {
            if (cell.CellType == CellType.Numeric)
                return Convert.ToDecimal(cell.NumericCellValue);
            var s = cell.ToString()?.Replace("'", "").Replace(" ", "").Trim() ?? "";
            return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;
        }
        catch { return 0; }
    }

    // ── Namen-Matching ───────────────────────────────────────────────────────
    // Mirus-Format: „Nachname Vorname" (Compound-Nachnamen sind möglich, z.B.
    // „Artiles Santana Aurelio"). Strategie: token-basiert auf Set-Gleichheit
    // (case-insensitive). Bei 0 exakten Treffern: Subset-Match. Resultat:
    // Liste der MA-Kandidaten (0 = NO_MATCH, 1 = MATCH, >1 = AMBIGUOUS).
    private static List<BranchEmployee> MatchByName(string mirusName, List<BranchEmployee> branchEmps)
    {
        var mirusTokens = Tokenize(mirusName);
        if (mirusTokens.Count == 0) return new();

        var exact = branchEmps.Where(b =>
        {
            var dbTokens = Tokenize(b.FirstName + " " + b.LastName);
            return SameTokenSet(mirusTokens, dbTokens);
        }).ToList();
        if (exact.Count >= 1) return exact;

        // Subset-Match: alle Mirus-Tokens müssen im DB-Namen vorkommen — fängt
        // Mittelnamen oder zusätzliche Vornamen, die nur eine Seite kennt.
        return branchEmps.Where(b =>
        {
            var dbTokens = Tokenize(b.FirstName + " " + b.LastName).ToHashSet();
            return mirusTokens.All(t => dbTokens.Contains(t));
        }).ToList();
    }

    private static List<string> Tokenize(string s) =>
        (s ?? "").Split(new[] { ' ', '-', ',', '.', '\t', '/' },
                        StringSplitOptions.RemoveEmptyEntries)
                 .Select(x => x.ToLowerInvariant())
                 .ToList();

    private static bool SameTokenSet(List<string> a, List<string> b)
    {
        if (a.Count != b.Count) return false;
        var sa = a.ToHashSet();
        var sb = b.ToHashSet();
        return sa.SetEquals(sb);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // STUNDEN-SALDI Import (Mirus „Monatsblatt", Walter-Vorgabe 26.05.2026)
    //
    // Quelle: Mirus „Monatsblatt <Monat> <Jahr>" (JasperReports-XLS). Format
    // pro MA: Header-Block + Tagesliste + Summary-Block. Pro MA werden zwei
    // Saldo-Werte extrahiert (jeweils col 63 = letzte Summary-Spalte „Saldo"):
    //   • Zeile mit col 1 = „Überstunden"  → Stunden-Saldo  (Vortrag-Code 901)
    //   • Zeile mit col 1 = „Zeitzuschlag" → Nacht-Saldo    (Vortrag-Code 904)
    //
    // Werte sind reine DEZIMALSTUNDEN (Mirus-Konvention 1/100, nicht hh:mm).
    // Match: per Personalnummer (col 12 der „Personalnummer:"-Zeile) — robuster
    // als Namen-Match. Vertragsmodell-Relevanz: 901+904 nur für MTP/FIX/FIX-M
    // (UTP führt keinen Stunden- oder Nacht-Saldo; ein bestehender Eintrag
    // würde wie im CHF-Pfad entfernt). Bestehende Vortrag-Codes 902/903/905/906
    // bleiben UNANGETASTET.
    // ══════════════════════════════════════════════════════════════════════════

    public record ParsedStundenRow(
        string  EmployeeNumber,
        string  Name,
        decimal? StundenSaldo,    // null = Zeile „Überstunden" im Block nicht vorhanden
        decimal? NachtSaldo       // null = Zeile „Zeitzuschlag" im Block nicht vorhanden
    );

    public record StundenAnalyzeRow(
        string  EmployeeNumber,
        string  Name,
        decimal? StundenSaldo,
        decimal? NachtSaldo,
        int?    EmployeeId,
        string? EmployeeMatchedName,
        string? EmploymentModel,
        string  Status   // MATCH / NO_MATCH
    );

    public record StundenAnalyzeResult(
        string                     Periode,
        int                        CompanyProfileId,
        int                        Total,
        int                        Matched,
        int                        NoMatch,
        List<StundenAnalyzeRow>    Rows,
        List<BranchEmployee>       BranchEmployees
    );

    public record StundenCommitRow(
        int      EmployeeId,
        decimal? StundenSaldo,
        decimal? NachtSaldo,
        string?  OriginalName
    );

    public record StundenCommitDto(
        int                      CompanyProfileId,
        string                   Periode,
        List<StundenCommitRow>   Rows
    );

    [HttpPost("stunden/analyze")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> AnalyzeStunden(
        [FromQuery] int    companyProfileId,
        [FromQuery] string periode,
        [FromForm]  IFormFile file)
    {
        if (file is null || file.Length == 0) return BadRequest(new { error = "Datei fehlt." });
        if (string.IsNullOrEmpty(periode) || periode.Length != 7 || periode[4] != '-')
            return BadRequest(new { error = "Periode muss im Format YYYY-MM sein." });

        List<ParsedStundenRow> rows;
        try { rows = ParseMonatsblatt(file); }
        catch (Exception ex) { return BadRequest(new { error = "Datei konnte nicht gelesen werden: " + ex.Message }); }

        // Branch-MA (für Match per Personalnummer + Picker bei NO_MATCH).
        var branchEmps = await _db.Employees
            .Where(e => e.IsActive && e.Employments.Any(em => em.CompanyProfileId == companyProfileId))
            .Select(e => new BranchEmployee(
                e.Id,
                e.FirstName ?? "",
                e.LastName ?? "",
                e.EmployeeNumber,
                e.Employments
                    .Where(em => em.IsActive && em.CompanyProfileId == companyProfileId)
                    .OrderByDescending(em => em.ContractStartDate)
                    .Select(em => em.EmploymentModel)
                    .FirstOrDefault()))
            .ToListAsync();

        // Match per Personalnummer (exakte Übereinstimmung).
        var byNumber = branchEmps
            .Where(b => !string.IsNullOrEmpty(b.EmployeeNumber))
            .ToDictionary(b => b.EmployeeNumber!.Trim(), b => b);

        var analyzeRows = new List<StundenAnalyzeRow>();
        foreach (var r in rows)
        {
            byNumber.TryGetValue(r.EmployeeNumber, out var emp);
            analyzeRows.Add(new StundenAnalyzeRow(
                r.EmployeeNumber, r.Name, r.StundenSaldo, r.NachtSaldo,
                emp?.Id,
                emp != null ? $"{emp.FirstName} {emp.LastName}".Trim() : null,
                emp?.EmploymentModel,
                emp != null ? "MATCH" : "NO_MATCH"));
        }

        return Ok(new StundenAnalyzeResult(
            Periode:          periode,
            CompanyProfileId: companyProfileId,
            Total:            analyzeRows.Count,
            Matched:          analyzeRows.Count(x => x.Status == "MATCH"),
            NoMatch:          analyzeRows.Count(x => x.Status == "NO_MATCH"),
            Rows:             analyzeRows,
            BranchEmployees:  branchEmps
                .OrderBy(b => b.FirstName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(b => b.LastName, StringComparer.OrdinalIgnoreCase)
                .ToList()));
    }

    [HttpPost("stunden/commit")]
    public async Task<IActionResult> CommitStunden([FromBody] StundenCommitDto dto)
    {
        if (dto is null) return BadRequest(new { error = "Body fehlt." });
        if (string.IsNullOrEmpty(dto.Periode) || dto.Periode.Length != 7 || dto.Periode[4] != '-')
            return BadRequest(new { error = "Periode ungültig." });
        if (dto.Rows is null || dto.Rows.Count == 0)
            return BadRequest(new { error = "Keine Zeilen zum Speichern." });

        // Vortrag-Lohnpositionen 901 (Stunden) + 904 (Nacht).
        var lps = await _db.Lohnpositionen
            .Where(l => l.Kategorie == "Saldo-Vortrag" && (l.Code == "901" || l.Code == "904"))
            .ToDictionaryAsync(l => l.Code, l => l);

        if (!lps.ContainsKey("901") || !lps.ContainsKey("904"))
            return Problem("Vortrag-Lohnpositionen 901/904 fehlen. Bitte add_saldo_vortrag.sql ausführen.", statusCode: 500);

        var empIds = dto.Rows.Select(r => r.EmployeeId).Distinct().ToList();
        var emps = await _db.Employees
            .Include(e => e.Employments)
            .Where(e => empIds.Contains(e.Id))
            .ToListAsync();

        var existing = await _db.LohnZulagen
            .Include(z => z.Lohnposition)
            .Where(z => empIds.Contains(z.EmployeeId)
                     && z.Lohnposition!.Kategorie == "Saldo-Vortrag"
                     && (z.Lohnposition.Code == "901" || z.Lohnposition.Code == "904"))
            .ToListAsync();

        int created = 0, updated = 0, skipped = 0;
        var hinweise = new List<string>();

        foreach (var row in dto.Rows)
        {
            var emp = emps.FirstOrDefault(e => e.Id == row.EmployeeId);
            if (emp is null) { skipped++; hinweise.Add($"MA-ID {row.EmployeeId} nicht gefunden ({row.OriginalName})."); continue; }

            var activeEmp = emp.Employments.Where(e => e.IsActive)
                .OrderByDescending(e => e.ContractStartDate).FirstOrDefault();
            var model = (activeEmp?.EmploymentModel ?? "").ToUpperInvariant();

            UpsertHours("901", lps["901"], row.StundenSaldo, IsRelevant901(model));
            UpsertHours("904", lps["904"], row.NachtSaldo,    IsRelevant904(model));

            void UpsertHours(string code, Lohnposition lp, decimal? value, bool relevant)
            {
                var ex = existing.FirstOrDefault(e =>
                    e.EmployeeId == row.EmployeeId && e.Lohnposition!.Code == code);

                if (!relevant)
                {
                    // Modell hat diesen Saldo nicht → bestehenden Eintrag entfernen
                    // (z.B. UTP hat keinen Stunden-/Nacht-Saldo).
                    if (ex != null) _db.LohnZulagen.Remove(ex);
                    return;
                }
                if (value is null)
                {
                    // Block hatte diese Zeile nicht — Wert NICHT setzen (bestehenden
                    // Eintrag intakt lassen). Anders als bei `!relevant`, wo wir
                    // löschen, ist `value==null` ein „keine Info, nicht anfassen".
                    return;
                }

                var betrag = Math.Round(value.Value, 2);
                if (ex == null)
                {
                    _db.LohnZulagen.Add(new LohnZulage
                    {
                        EmployeeId     = row.EmployeeId,
                        Periode        = dto.Periode,
                        LohnpositionId = lp.Id,
                        Betrag         = betrag,
                        Bemerkung      = "Migrations-Vortrag aus Mirus Monatsblatt (Stunden)",
                        CreatedAt      = DateTime.UtcNow,
                        UpdatedAt      = DateTime.UtcNow
                    });
                    created++;
                }
                else
                {
                    ex.Periode   = dto.Periode;
                    ex.Betrag    = betrag;
                    ex.UpdatedAt = DateTime.UtcNow;
                    updated++;
                }
            }
        }

        await _db.SaveChangesAsync();
        return Ok(new CommitResult(created, updated, skipped, hinweise));
    }

    private static bool IsRelevant901(string model) =>    // Zeitsaldo (Stunden)
        model == "MTP" || model == "FIX" || model == "FIX-M";
    private static bool IsRelevant904(string model) =>    // Nacht-Saldo
        model == "MTP" || model == "FIX" || model == "FIX-M";

    /// <summary>
    /// Parser für das Mirus „Monatsblatt" (JasperReports-XLS). Layout pro MA:
    ///   Anker-Zeile: col 2 == „Personalnummer:" → Name in (row-1, col 12),
    ///                Nummer in (row, col 12), Kostenstelle in (row+1, col 12).
    ///   Innerhalb des Blocks (bis zum nächsten Personalnummer:-Anker oder
    ///   Dateiende) suchen wir zwei Zeilen mit Label in col 1:
    ///     • „Überstunden"  → Saldo in col 63 → Stunden-Saldo
    ///     • „Zeitzuschlag" → Saldo in col 63 → Nacht-Saldo
    /// Fehlt eine der Label-Zeilen, ist der jeweilige Saldo null (UTP-MA haben
    /// typischerweise keine Überstunden-Zeile).
    /// </summary>
    private static List<ParsedStundenRow> ParseMonatsblatt(IFormFile file)
    {
        using var stream = file.OpenReadStream();
        IWorkbook wb;
        try
        {
            wb = file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
                ? new XSSFWorkbook(stream)
                : new HSSFWorkbook(stream);
        }
        catch
        {
            stream.Position = 0;
            wb = new XSSFWorkbook(stream);
        }
        var sh = wb.GetSheetAt(0);

        // 1) Alle MA-Block-Anker finden.
        var anchors = new List<(int Row, string Name, string Number)>();
        for (int r = 0; r <= sh.LastRowNum; r++)
        {
            var row = sh.GetRow(r);
            if (row is null) continue;
            var label = (row.GetCell(2)?.ToString() ?? "").Trim();
            if (label != "Personalnummer:") continue;

            var name  = (sh.GetRow(r - 1)?.GetCell(12)?.ToString() ?? "").Trim();
            var nrCell = row.GetCell(12);
            string nr;
            if (nrCell?.CellType == CellType.Numeric)
                nr = ((long)nrCell.NumericCellValue).ToString();
            else
                nr = (nrCell?.ToString() ?? "").Trim();

            if (!string.IsNullOrEmpty(nr)) anchors.Add((r, name, nr));
        }

        // 2) Pro Anker im Block-Bereich Überstunden + Zeitzuschlag suchen.
        var result = new List<ParsedStundenRow>();
        for (int i = 0; i < anchors.Count; i++)
        {
            var (start, name, nr) = anchors[i];
            var end = (i + 1 < anchors.Count) ? anchors[i + 1].Row : sh.LastRowNum + 1;

            decimal? stunden = null, nacht = null;
            for (int r = start; r < end; r++)
            {
                var row = sh.GetRow(r);
                if (row is null) continue;
                var lab = (row.GetCell(1)?.ToString() ?? "").Trim();
                if (lab == "Überstunden")  stunden = ReadDecimalNullable(row.GetCell(63));
                else if (lab == "Zeitzuschlag") nacht = ReadDecimalNullable(row.GetCell(63));
            }
            result.Add(new ParsedStundenRow(nr, name, stunden, nacht));
        }
        return result;
    }

    private static decimal? ReadDecimalNullable(ICell? cell)
    {
        if (cell is null) return null;
        try
        {
            if (cell.CellType == CellType.Numeric)
                return Convert.ToDecimal(cell.NumericCellValue);
            var s = (cell.ToString() ?? "").Replace("'", "").Replace(" ", "").Trim();
            if (string.IsNullOrEmpty(s)) return null;
            return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null;
        }
        catch { return null; }
    }
}
