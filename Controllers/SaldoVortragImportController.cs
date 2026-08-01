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
                        CreatedAt      = DateTime.Now,
                        UpdatedAt      = DateTime.Now
                    });
                    created++;
                }
                else
                {
                    ex.Periode   = dto.Periode;
                    ex.Betrag    = betrag;
                    ex.UpdatedAt = DateTime.Now;
                    updated++;
                }
            }
        }

        try { await _db.SaveChangesAsync(); }
        catch (Exception ex)
        {
            var root = ex;
            while (root.InnerException != null) root = root.InnerException;
            return BadRequest(new { error = "Speichern fehlgeschlagen: " + root.Message });
        }
        return Ok(new CommitResult(created, updated, skipped, hinweise));
    }

    // ── Vertragsmodell-Relevanz (analog SaldoVortragController) ─────────────

    private static bool IsRelevant905(string model) =>   // Ferien-Geld CHF
        model == "FLEX" || model == "MTP";
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
    // STUNDEN-/TAGE-SALDI Import (Mirus „Monatsblatt", Walter-Vorgabe 26.05.2026,
    // Spalten-Layout präzisiert 31.07.2026)
    //
    // Quelle: Mirus „Monatsblatt <Monat> <Jahr>" bzw. „Abrechnung individuelle
    // Position" (JasperReports-XLS). Format pro MA: Header + Tage-Block +
    // Tagesliste + Stunden-Summary. Pro MA extrahiert:
    //   • „Überstunden"  Saldo → Zeitsaldo     (901)  — Dezimalstunden
    //   • „Zeitzuschlag" Saldo → Nacht-Saldo   (904)  — Dezimalstunden
    //   • „Ferien"       Saldo → Ferien-Tage   (903)
    //   • „Feier"        Saldo → Feiertag-Tage (902)
    //
    // Spalten sind NICHT fest verdrahtet: JasperReports verschiebt Zellen je
    // nach Export (ältere Exporte: Name/PNr col 12, Stunden-Saldo col 63;
    // Sursee Juni-2026: Name/PNr col 14, Stunden-Saldo col 71, Tage-Saldo
    // col 61). Der Parser findet Anker + Saldo-Spalte dynamisch.
    // „Stunden" (FLEX-Ist) ≠ „Überstunden" — wird bewusst ignoriert.
    // Match per Personalnummer. Relevanz: 901/904 MTP/FIX/FIX-M; 902 FIX/FIX-M;
    // 903 alle Modelle. Codes 905/906 (CHF) bleiben unangetastet.
    // ══════════════════════════════════════════════════════════════════════════

    public record ParsedStundenRow(
        string   EmployeeNumber,
        string   Name,
        decimal? StundenSaldo,      // null = Zeile „Überstunden" fehlt
        decimal? NachtSaldo,        // null = Zeile „Zeitzuschlag" fehlt
        decimal? FerienTageSaldo,   // null = Zeile „Ferien" fehlt
        decimal? FeiertagTageSaldo  // null = Zeile „Feier" fehlt
    );

    public record StundenAnalyzeRow(
        string   EmployeeNumber,
        string   Name,
        decimal? StundenSaldo,
        decimal? NachtSaldo,
        decimal? FerienTageSaldo,
        decimal? FeiertagTageSaldo,
        int?     EmployeeId,
        string?  EmployeeMatchedName,
        string?  EmploymentModel,
        string   Status   // MATCH / NO_MATCH
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
        decimal? FerienTageSaldo,
        decimal? FeiertagTageSaldo,
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
                r.EmployeeNumber, r.Name,
                r.StundenSaldo, r.NachtSaldo, r.FerienTageSaldo, r.FeiertagTageSaldo,
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

        // Vortrag-Lohnpositionen 901/902/903/904 (Stunden, Feiertag-Tage, Ferien-Tage, Nacht).
        var codes = new[] { "901", "902", "903", "904" };
        Dictionary<string, Lohnposition> lps;
        try
        {
            var lpList = await _db.Lohnpositionen
                .Where(l => codes.Contains(l.Code))
                .ToListAsync();
            // Kategorie nachziehen falls Alt-Daten ohne «Saldo-Vortrag»
            foreach (var lp in lpList.Where(l => l.Kategorie != "Saldo-Vortrag"))
                lp.Kategorie = "Saldo-Vortrag";
            lps = lpList.GroupBy(l => l.Code).ToDictionary(g => g.Key, g => g.First());
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = "Vortrag-Lohnpositionen konnten nicht geladen werden: " + ex.Message });
        }

        var missing = codes.Where(c => !lps.ContainsKey(c)).ToList();
        if (missing.Count > 0)
            return BadRequest(new {
                error = $"Vortrag-Lohnpositionen fehlen in der DB: {string.Join(", ", missing)}. " +
                        "Bitte Server neu starten (Seed) oder add_saldo_vortrag.sql ausführen."
            });

        var empIds = dto.Rows.Select(r => r.EmployeeId).Distinct().ToList();
        var emps = await _db.Employees
            .Include(e => e.Employments)
            .Where(e => empIds.Contains(e.Id))
            .ToListAsync();

        var existing = await _db.LohnZulagen
            .Include(z => z.Lohnposition)
            .Where(z => empIds.Contains(z.EmployeeId)
                     && z.Lohnposition != null
                     && codes.Contains(z.Lohnposition.Code))
            .ToListAsync();

        int created = 0, updated = 0, skipped = 0;
        var hinweise = new List<string>();

        foreach (var row in dto.Rows)
        {
            var emp = emps.FirstOrDefault(e => e.Id == row.EmployeeId);
            if (emp is null) { skipped++; hinweise.Add($"MA-ID {row.EmployeeId} nicht gefunden ({row.OriginalName})."); continue; }

            var activeEmp = emp.Employments.Where(e => e.IsActive)
                .OrderByDescending(e => e.ContractStartDate).FirstOrDefault();
            var model = NormalizeModel(activeEmp?.EmploymentModel);

            UpsertSaldo("901", lps["901"], row.StundenSaldo,       IsRelevant901(model));
            UpsertSaldo("904", lps["904"], row.NachtSaldo,         IsRelevant904(model));
            UpsertSaldo("903", lps["903"], row.FerienTageSaldo,    IsRelevant903(model));
            UpsertSaldo("902", lps["902"], row.FeiertagTageSaldo,  IsRelevant902(model));

            void UpsertSaldo(string code, Lohnposition lp, decimal? value, bool relevant)
            {
                var ex = existing.FirstOrDefault(e =>
                    e.EmployeeId == row.EmployeeId && e.Lohnposition!.Code == code);

                if (!relevant)
                {
                    // Modell hat diesen Saldo nicht → bestehenden Eintrag entfernen
                    // (z.B. FLEX hat keinen Stunden-/Nacht-/Feiertag-Saldo).
                    if (ex != null) _db.LohnZulagen.Remove(ex);
                    return;
                }
                if (value is null)
                {
                    // Block hatte diese Zeile nicht — Wert NICHT setzen (bestehenden
                    // Eintrag intakt lassen). Anders als bei `!relevant`, wo wir
                    // löschen, ist `value==null` ein «keine Info, nicht anfassen».
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
                        Bemerkung      = "Migrations-Vortrag aus Mirus Monatsblatt",
                        CreatedAt      = DateTime.Now,
                        UpdatedAt      = DateTime.Now
                    });
                    created++;
                }
                else
                {
                    ex.Periode   = dto.Periode;
                    ex.Betrag    = betrag;
                    ex.UpdatedAt = DateTime.Now;
                    updated++;
                }
            }
        }

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            var root = ex;
            while (root.InnerException != null) root = root.InnerException;
            return BadRequest(new { error = "Speichern fehlgeschlagen: " + root.Message });
        }

        return Ok(new CommitResult(created, updated, skipped, hinweise));
    }

    /// <summary>Legacy «UTP» → FLEX (Rename 08.07.2026).</summary>
    private static string NormalizeModel(string? model)
    {
        var m = (model ?? "").Trim().ToUpperInvariant();
        return m == "UTP" ? "FLEX" : m;
    }

    private static bool IsRelevant901(string model) =>    // Zeitsaldo (Stunden)
        model is "MTP" or "FIX" or "FIX-M";
    private static bool IsRelevant904(string model) =>    // Nacht-Saldo
        model is "MTP" or "FIX" or "FIX-M";
    private static bool IsRelevant902(string model) =>    // Feiertag-Tage
        model is "FIX" or "FIX-M";
    private static bool IsRelevant903(string model) =>    // Ferien-Tage
        model is "FLEX" or "MTP" or "FIX" or "FIX-M";

    /// <summary>
    /// Parser für das Mirus «Monatsblatt» / «Abrechnung individuelle Position»
    /// (JasperReports-XLS). Spalten dynamisch — siehe Klassenkommentar oben.
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

        // 1) Alle MA-Block-Anker finden (Label «Personalnummer:» irgendwo in der Zeile).
        var anchors = new List<(int Row, string Name, string Number)>();
        for (int r = 0; r <= sh.LastRowNum; r++)
        {
            var row = sh.GetRow(r);
            if (row is null) continue;
            if (!TryFindLabelCell(row, "Personalnummer:", out int labelCol)) continue;

            var nr = ReadPersonalNumber(row, labelCol);
            if (string.IsNullOrEmpty(nr)) continue;

            var name = ReadNameAbove(sh, r, labelCol);
            anchors.Add((r, name, nr));
        }

        // 2) Pro Anker Saldi im Block suchen.
        var result = new List<ParsedStundenRow>();
        for (int i = 0; i < anchors.Count; i++)
        {
            var (start, name, nr) = anchors[i];
            var end = (i + 1 < anchors.Count) ? anchors[i + 1].Row : sh.LastRowNum + 1;

            decimal? stunden = null, nacht = null, ferien = null, feier = null;
            int? hoursSaldoCol = null, daysSaldoCol = null;

            for (int r = start; r < end; r++)
            {
                var row = sh.GetRow(r);
                if (row is null) continue;

                // Saldo-Spalte aus Header-Zeilen merken (Tage- und Stunden-Block).
                // Beide Header haben «Vortr.» — Unterscheidung:
                //   Tage:   enthält «Soll» und/oder «Zus. Tage»
                //   Stunden: enthält «(Abw.)» / «Komp.» und KEIN «Soll»
                if (TryFindLabelCell(row, "Saldo", out int saldoCol))
                {
                    // Tage-Header: «Soll» / «Zus. Tage» (Sursee col 61, Oftringen col 54)
                    // Stunden-Header: «(Abw.)» / «Komp.» ohne «Soll» (col 71 bzw. 63)
                    bool looksLikeDays = CellTextEquals(row, "Soll")
                                      || CellTextEquals(row, "Zus. Tage");
                    bool looksLikeHours = !looksLikeDays
                                      && (CellTextEquals(row, "(Abw.)")
                                          || CellTextEquals(row, "Komp.")
                                          || CellTextEquals(row, "Komp")
                                          || CellTextEquals(row, "Soll Netto"));
                    if (looksLikeHours) hoursSaldoCol = saldoCol;
                    else if (looksLikeDays) daysSaldoCol = saldoCol;
                    else
                    {
                        // Unklare Header-Zeile: erste = Tage, spätere = Stunden
                        // (Tage-Block steht in beiden bekannten Layouts oben).
                        if (daysSaldoCol is null) daysSaldoCol = saldoCol;
                        else hoursSaldoCol = saldoCol;
                    }
                }

                // Label in den ersten Spalten (Jasper legt «Überstunden»/«Ferien» oft in col 1 bzw. 4).
                var lab = FirstNonEmptyLabel(row);
                if (string.IsNullOrEmpty(lab)) continue;

                if (lab == "Überstunden")
                    stunden = ReadSaldo(row, hoursSaldoCol, preferredFallbacks: new[] { 71, 63, 54 });
                else if (lab == "Zeitzuschlag")
                    nacht = ReadSaldo(row, hoursSaldoCol, preferredFallbacks: new[] { 71, 63, 54 });
                else if (lab == "Ferien")
                    ferien = ReadSaldo(row, daysSaldoCol, preferredFallbacks: new[] { 61, 54, 63 });
                else if (lab == "Feier" || lab.StartsWith("Feier", StringComparison.Ordinal))
                    feier = ReadSaldo(row, daysSaldoCol, preferredFallbacks: new[] { 61, 54, 63 });
            }

            result.Add(new ParsedStundenRow(nr, name, stunden, nacht, ferien, feier));
        }
        return result;
    }

    /// <summary>Sucht eine Zelle deren Trim-Text exakt <paramref name="label"/> ist.</summary>
    private static bool TryFindLabelCell(IRow row, string label, out int col)
    {
        col = -1;
        short last = row.LastCellNum;
        if (last < 0) return false;
        for (int c = 0; c < last; c++)
        {
            var t = (row.GetCell(c)?.ToString() ?? "").Trim();
            if (t == label) { col = c; return true; }
        }
        return false;
    }

    private static bool CellTextEquals(IRow row, string text)
    {
        short last = row.LastCellNum;
        if (last < 0) return false;
        for (int c = 0; c < last; c++)
        {
            if ((row.GetCell(c)?.ToString() ?? "").Trim() == text) return true;
        }
        return false;
    }

    private static string FirstNonEmptyLabel(IRow row)
    {
        // Labels stehen in den linken Spalten (0–6); Zahlen/Daten weiter rechts.
        for (int c = 0; c <= 6; c++)
        {
            var t = (row.GetCell(c)?.ToString() ?? "").Trim();
            if (string.IsNullOrEmpty(t)) continue;
            // Excel-Datums-Seriennummern / reine Zahlen überspringen
            if (double.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out _)) continue;
            return t;
        }
        return "";
    }

    private static string ReadPersonalNumber(IRow row, int labelCol)
    {
        // Wert steht rechts vom Label — typisch gleiche «Wert-Spalte» wie Name (12 oder 14).
        short last = row.LastCellNum;
        for (int c = labelCol + 1; c < Math.Max(last, labelCol + 20); c++)
        {
            var cell = row.GetCell(c);
            if (cell is null) continue;
            if (cell.CellType == CellType.Numeric)
            {
                var n = cell.NumericCellValue;
                if (n <= 0) continue;
                return Math.Abs(n % 1) < 0.0000001
                    ? ((long)Math.Round(n)).ToString(CultureInfo.InvariantCulture)
                    : n.ToString(CultureInfo.InvariantCulture);
            }
            var s = (cell.ToString() ?? "").Trim();
            if (string.IsNullOrEmpty(s)) continue;
            // «Personalnummer:750009» falls Label+Wert in einer Zelle
            if (s.Contains(':')) s = s.Split(':').Last().Trim();
            if (!string.IsNullOrEmpty(s)) return s;
        }
        return "";
    }

    private static string ReadNameAbove(ISheet sh, int personalNrRow, int labelCol)
    {
        var above = sh.GetRow(personalNrRow - 1);
        if (above is null) return "";

        // Bevorzugt: Zeile mit Label «Name / Vorname» / «Name» → Wert rechts daneben.
        if (TryFindLabelCell(above, "Name / Vorname", out int nameLabel)
            || TryFindLabelCell(above, "Name", out nameLabel))
        {
            short last = above.LastCellNum;
            for (int c = nameLabel + 1; c < Math.Max(last, nameLabel + 20); c++)
            {
                var s = (above.GetCell(c)?.ToString() ?? "").Trim();
                if (!string.IsNullOrEmpty(s)) return s;
            }
        }

        // Fallback: gleiche Wert-Spalte wie die Personalnummer (historisch 12).
        foreach (int c in new[] { 14, 12, labelCol + 12, labelCol + 10 })
        {
            var s = (above.GetCell(c)?.ToString() ?? "").Trim();
            if (!string.IsNullOrEmpty(s) && !s.StartsWith("Name", StringComparison.OrdinalIgnoreCase))
                return s;
        }
        return "";
    }

    private static decimal? ReadSaldo(IRow row, int? detectedCol, int[] preferredFallbacks)
    {
        if (detectedCol is int dc)
        {
            var v = ReadDecimalNullable(row.GetCell(dc));
            if (v is not null) return v;
        }
        foreach (var c in preferredFallbacks)
        {
            var v = ReadDecimalNullable(row.GetCell(c));
            if (v is not null) return v;
        }
        return null;
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
