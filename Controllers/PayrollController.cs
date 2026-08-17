using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static HrSystem.Services.PayrollCalculations;

namespace HrSystem.Controllers;

[ApiController]
[Route("api/payroll")]
public class PayrollController : HrControllerBase
{
    // GetCurrentUserId() + CanAccessBranchAsync() leben jetzt in HrControllerBase.
    // GetUserIdOrNull() bleibt als 1-Zeilen-Alias erhalten, damit die bestehenden
    // Aufrufstellen (HrBestaetigtBy, GfFreigegebenBy …) unverändert weiterlaufen.
    private int? GetUserIdOrNull() => GetCurrentUserId();

    private readonly QuellensteuerTarifService _tarifService;
    private readonly KtgTagessatzService _ktgService;
    private readonly KarenzService _karenz;
    private readonly LgavBeitragService _lgav;
    private readonly UniformDepotService _uniformDepot;
    private readonly FerienKuerzungService _ferienKuerzung;
    private readonly PayrollPdfService _payrollPdf;
    private readonly StundenkontrollePdfService _stundenkontrollePdf;
    private readonly PayrollCalculationEngine _calcEngine;
    private readonly MinimumWageCheckService _minWage;
    private readonly QstPflichtCheckService _qstCheck;
    private readonly LohnSaldoListePdfService _saldoListePdf;
    private readonly FibuJournalService _fibuJournal;
    private readonly SnapshotRecomputeService _snapshotRecompute;
    private readonly AuswertungenReportPdfService _auswertungenPdf;

    public PayrollController(
        AppDbContext db,
        QuellensteuerTarifService tarifService,
        KtgTagessatzService ktgService,
        KarenzService karenz,
        LgavBeitragService lgav,
        UniformDepotService uniformDepot,
        FerienKuerzungService ferienKuerzung,
        PayrollPdfService payrollPdf,
        StundenkontrollePdfService stundenkontrollePdf,
        PayrollCalculationEngine calcEngine,
        MinimumWageCheckService minWage,
        QstPflichtCheckService qstCheck,
        LohnSaldoListePdfService saldoListePdf,
        FibuJournalService fibuJournal,
        SnapshotRecomputeService snapshotRecompute,
        AuswertungenReportPdfService auswertungenPdf) : base(db)
    {
        _tarifService   = tarifService;
        _ktgService     = ktgService;
        _karenz         = karenz;
        _lgav           = lgav;
        _uniformDepot   = uniformDepot;
        _ferienKuerzung = ferienKuerzung;
        _payrollPdf     = payrollPdf;
        _stundenkontrollePdf = stundenkontrollePdf;
        _calcEngine     = calcEngine;
        _minWage        = minWage;
        _qstCheck       = qstCheck;
        _saldoListePdf  = saldoListePdf;
        _fibuJournal    = fibuJournal;
        _snapshotRecompute = snapshotRecompute;
        _auswertungenPdf = auswertungenPdf;
    }

    // Buchungslisten (Fibu-Journal, Saldo-Listen) gibt es erst, wenn der
    // DEFINITIV-Lauf der Periode mindestens "provisorisch_abgeschlossen" ist.
    // Solange er "offen" ist (oder die Periode fehlt), liefern die Endpoints
    // nichts — sonst zeigte ein zurückgesetzter Lohnlauf weiterhin Buchungen
    // aus den noch vorhandenen Snapshots (Walter-Vorgabe 25.05.2026: „zurück-
    // gesetzt = Listen leer"). Der Akonto-Status spielt keine Rolle.
    private async Task<bool> IsDefinitivConfirmedAsync(int companyProfileId, int year, int month)
    {
        var status = await _db.PayrollPerioden
            .Where(p => p.CompanyProfileId == companyProfileId && p.Year == year && p.Month == month)
            .Select(p => p.Status)
            .FirstOrDefaultAsync();
        return status == "provisorisch_abgeschlossen" || status == "abgeschlossen";
    }

    private IActionResult PeriodeNichtAbgeschlossen() => Conflict(new {
        error   = "PERIODE_NICHT_ABGESCHLOSSEN",
        message = "Diese Buchungsliste ist erst verfügbar, wenn der Definitiv-Lohnlauf der Periode (mindestens provisorisch) abgeschlossen ist. Der Lauf ist aktuell offen."
    });

    // GET /api/payroll/fibu-journal?companyProfileId&year&month  → Fibu-Journal-PDF
    // (Buchungsjournal aus den bestätigten Snapshots, Walter 22.05.2026). HR-only.
    [HttpGet("fibu-journal")]
    [Authorize(Roles = "admin,buchhaltung")]   // Fibu-Bereich: nur Buchhaltung + admin (nicht reine HR/superuser)
    public async Task<IActionResult> FibuJournal(
        [FromQuery] int companyProfileId, [FromQuery] int year, [FromQuery] int month)
    {
        if (!await CanAccessBranchAsync(companyProfileId))
            return StatusCode(403, new { error = "Kein Zugriff auf diese Filiale." });
        if (!await IsDefinitivConfirmedAsync(companyProfileId, year, month))
            return PeriodeNichtAbgeschlossen();
        try
        {
            var pdf = await _fibuJournal.GeneratePdfAsync(companyProfileId, year, month);
            return File(pdf, "application/pdf");
        }
        catch (InvalidOperationException ex) { return NotFound(new { error = ex.Message }); }
    }

    // GET /api/payroll/fibu-abaconnect?companyProfileId&year&month
    // → Abacus-Export (E3, Walter 04.08.2026): dasselbe Journal als AbaConnect-
    // XML «FIBU / XML Buchungen» v2014.00 (Vorlage = Mirus-Export). Direkt-
    // Download (kein Vorschaufenster — XML ist eine Import-Datei wie DTA/LSE).
    // Gleiche Guards wie fibu-journal: Rolle, Filial-Zugriff, Definitiv-Riegel.
    [HttpGet("fibu-abaconnect")]
    [Authorize(Roles = "admin,buchhaltung")]
    public async Task<IActionResult> FibuAbaConnect(
        [FromQuery] int companyProfileId, [FromQuery] int year, [FromQuery] int month,
        [FromQuery] string? entryDate = null, [FromQuery] string? documentNumber = null)
    {
        if (!await CanAccessBranchAsync(companyProfileId))
            return StatusCode(403, new { error = "Kein Zugriff auf diese Filiale." });
        if (!await IsDefinitivConfirmedAsync(companyProfileId, year, month))
            return PeriodeNichtAbgeschlossen();
        // FIBU-Buchungsdatum wählbar (Treuhänder-Empfehlung 04.08.2026);
        // leer/ungültig → Periodenende als Default (im Service).
        DateOnly? buchungsdatum = null;
        if (!string.IsNullOrWhiteSpace(entryDate)
            && DateOnly.TryParseExact(entryDate, "yyyy-MM-dd", out var ed))
            buchungsdatum = ed;

        // Abacus-Buchungsnummer (Treuhänder-Vorgabe 05.08.2026): pro Periode
        // persistieren — mitgegebene Nummer speichern, ohne Angabe die
        // gespeicherte verwenden (Re-Export = gleiche Nummer).
        var periode = await _db.PayrollPerioden.FirstOrDefaultAsync(
            p => p.CompanyProfileId == companyProfileId && p.Year == year && p.Month == month);
        var docNr = string.IsNullOrWhiteSpace(documentNumber)
            ? periode?.FibuBuchungsnummer
            : documentNumber.Trim();
        if (periode != null && !string.IsNullOrWhiteSpace(docNr) && periode.FibuBuchungsnummer != docNr)
        {
            periode.FibuBuchungsnummer = docNr;
            await _db.SaveChangesAsync();
        }

        try
        {
            var (xml, journal) = await _fibuJournal.GenerateAbaConnectXmlAsync(
                companyProfileId, year, month, buchungsdatum, docNr);
            // Sicherung: ein unbalanciertes Journal darf NICHT nach Abacus —
            // 1920-Differenz > Rundungs-Toleranz deutet auf Snapshot-Fehler.
            if (Math.Abs(journal.Konto1920Saldo) > 0.05m)
                return Conflict(new
                {
                    error = "JOURNAL_NICHT_BALANCIERT",
                    message = $"Konto 1920 geht nicht auf (Differenz {journal.Konto1920Saldo:0.00}). " +
                              "Bitte zuerst das Fibu-Journal prüfen (Hinweise), dann erneut exportieren."
                });
            return File(xml, "application/xml",
                $"AbaConnect-Fibu_{companyProfileId}_{year}-{month:D2}.xml");
        }
        catch (InvalidOperationException ex) { return NotFound(new { error = ex.Message }); }
    }

    // POST /api/payroll/refresh-snapshot-codes?companyProfileId&year&month  (admin-only)
    // Wartung (Walter 22.05.2026): trägt die Fibu-Codes (categoryCode/code) in
    // bestehende Snapshots nach — nötig fürs Fibu-Journal bei Perioden, die VOR
    // dem Code-Feature bestätigt wurden.
    //
    // WICHTIG (amount-preserving): der frisch berechnete Slip dient NUR als
    // Code-Quelle. Pro existierender Abzugs-/Lohnzeile wird per Bezeichnung die
    // passende frische Zeile gesucht und NUR categoryCode/code übernommen — der
    // gespeicherte `betrag` bleibt unangetastet. Würden wir die Arrays wholesale
    // ersetzen, könnte ein seit dem Bestätigen veränderter Wert (z. B. LGAV, das
    // EnsureAsync nachträglich pro MA ergänzt — EnsureAsync committet selbst) in
    // den eingefrorenen Snapshot sickern → Durchlaufkonto 1920 ginge nicht mehr
    // auf. Frozen bleibt frozen; das Journal spiegelt exakt das Bestätigte.
    [HttpPost("refresh-snapshot-codes")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> RefreshSnapshotCodes(
        [FromQuery] int companyProfileId, [FromQuery] int year, [FromQuery] int month)
    {
        var periode = await _db.PayrollPerioden
            .FirstOrDefaultAsync(p => p.CompanyProfileId == companyProfileId && p.Year == year && p.Month == month);
        if (periode == null) return NotFound(new { error = "Periode nicht gefunden." });

        var snaps = await _db.PayrollSnapshots
            .Where(s => s.PayrollPeriodeId == periode.Id && s.Status != "STORNIERT")
            .ToListAsync();

        var camel = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        int updated = 0;
        foreach (var s in snaps)
        {
            var calc = await _calcEngine.CalculateAsync(s.EmployeeId, year, month, companyProfileId);
            if (calc is not OkObjectResult ok || ok.Value is null) continue;

            JsonNode? fresh;
            JsonNode? existing;
            try { fresh    = JsonNode.Parse(JsonSerializer.Serialize(ok.Value, camel)); }
            catch { continue; }
            try { existing = JsonNode.Parse(string.IsNullOrWhiteSpace(s.SlipJson) ? "{}" : s.SlipJson); }
            catch { continue; }
            if (fresh is null || existing is null) continue;

            bool changed = MergeLineCodes(existing["abzugLines"], fresh["abzugLines"]);
            changed     |= MergeLineCodes(existing["lohnLines"],  fresh["lohnLines"]);
            if (!changed) continue;

            s.SlipJson  = existing.ToJsonString();
            s.UpdatedAt = DateTime.Now; // System-Regel: Lokalzeit + timestamp without time zone (Walter 04.08.2026)
            updated++;
        }
        await _db.SaveChangesAsync();
        return Ok(new { updated, total = snaps.Count });
    }

    // Überträgt categoryCode/code von den frischen Zeilen auf die bestehenden,
    // gematcht über die Bezeichnung. Verändert KEINE Beträge. Liefert true, wenn
    // mindestens ein Code gesetzt wurde.
    private static bool MergeLineCodes(JsonNode? existingArr, JsonNode? freshArr)
    {
        if (existingArr is not JsonArray ex || freshArr is not JsonArray fr) return false;
        bool changed = false;
        foreach (var exNode in ex)
        {
            if (exNode is not JsonObject exObj) continue;
            var bez = exObj["bezeichnung"]?.GetValue<string>();
            if (string.IsNullOrEmpty(bez)) continue;

            foreach (var frNode in fr)
            {
                if (frNode is not JsonObject frObj) continue;
                if (frObj["bezeichnung"]?.GetValue<string>() != bez) continue;

                if (exObj["categoryCode"] is null && frObj["categoryCode"] is JsonNode cc)
                { exObj["categoryCode"] = cc.DeepClone(); changed = true; }
                if (exObj["code"] is null && frObj["code"] is JsonNode cd)
                { exObj["code"] = cd.DeepClone(); changed = true; }
                break;
            }
        }
        return changed;
    }

    // POST /api/payroll/recompute-snapshots?companyProfileId&year&month  (admin-only)
    // Reparatur-Werkzeug (Walter 22.05.2026): rechnet jeden Snapshot der Periode
    // via CalculateAsync NEU und überschreibt Brutto + Netto + SlipJson GEMEINSAM
    // aus EINER frischen Rechnung. Der Workflow-Status bleibt.
    //
    // Anlass: ein früherer Refresh hatte NUR SlipJson überschrieben, nicht aber
    // Brutto/Netto → Snapshot und Slip liefen auseinander (z.B. LGAV 2'970 im Slip
    // vs. 2'123.94 im eingefrorenen Netto) → Durchlaufkonto 1920 ging nicht auf.
    // Hier werden alle drei zusammen gesetzt, damit Brutto = Netto + Abzüge gilt.
    //
    // ACHTUNG: überschreibt die eingefrorenen Beträge mit der aktuellen Rechnung.
    // Nur sinnvoll, wenn die Periode noch NICHT effektiv (DTA) ausbezahlt ist.
    [HttpPost("recompute-snapshots")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> RecomputeSnapshots(
        [FromQuery] int companyProfileId, [FromQuery] int year, [FromQuery] int month)
    {
        var periode = await _db.PayrollPerioden
            .FirstOrDefaultAsync(p => p.CompanyProfileId == companyProfileId && p.Year == year && p.Month == month);
        if (periode == null) return NotFound(new { error = "Periode nicht gefunden." });

        var total = await _db.PayrollSnapshots
            .CountAsync(s => s.PayrollPeriodeId == periode.Id && s.Status != "STORNIERT");
        try
        {
            var updated = await _snapshotRecompute.RecomputeAsync(companyProfileId, year, month);
            return Ok(new { updated, total });
        }
        catch (Exception ex)
        {
            // Klartext statt nacktem 500. Snapshot/Saldo folgen seit 04.08.2026
            // der System-Regel: Lokalzeit (DateTime.Now) + timestamp without time
            // zone (Migration fix_payroll_snapshot_saldo_timestamps.sql).
            return StatusCode(500, new {
                error = "RECOMPUTE_FAILED",
                message = ex.GetBaseException().Message
            });
        }
    }

    /// <summary>
    /// Uniformen-Depot CHF 50 für alle Erst-Löhne / Eintritte der Periode
    /// nachziehen (Walter Aug 2026). Feature kam oft erst NACH der
    /// Lohnbestätigung — legt LohnZulage 600.32 an und rechnet betroffene
    /// Snapshots neu (Status bleibt). Idempotent.
    /// </summary>
    [HttpPost("ensure-uniform-depots")]
    [Authorize(Roles = "admin,superuser,buchhaltung")]
    public async Task<IActionResult> EnsureUniformDepots(
        [FromQuery] int companyProfileId, [FromQuery] int year, [FromQuery] int month)
    {
        if (!await CanAccessBranchAsync(companyProfileId))
            return StatusCode(403, new { error = "Kein Zugriff auf diese Filiale." });

        var periode = await _db.PayrollPerioden
            .FirstOrDefaultAsync(p => p.CompanyProfileId == companyProfileId && p.Year == year && p.Month == month);
        if (periode != null && string.Equals(periode.Status, "abgeschlossen", StringComparison.OrdinalIgnoreCase))
        {
            return Conflict(new
            {
                error = "PERIODE_ABGESCHLOSSEN",
                message = "Periode ist definitiv abgeschlossen — Depot-Nachzug nicht mehr möglich."
            });
        }

        var (charged, employeeIds) = await _uniformDepot.EnsureChargesForPeriodAsync(companyProfileId, year, month);
        int recomputed = 0;
        if (charged > 0)
            recomputed = await _snapshotRecompute.RecomputeAsync(companyProfileId, year, month);

        return Ok(new
        {
            charged,
            employeeIds,
            recomputed,
            message = charged > 0
                ? $"Uniformen-Depot: {charged} Eintritt(e) nachgezogen, {recomputed} Snapshot(s) neu gerechnet."
                : "Keine neuen Depot-Abzüge nötig."
        });
    }

    // ── Saldo-Listen zum Definitiv-Abschluss (Walter-Vorgabe 21.05.2026) ──────
    // Zwei PDFs pro Filiale + Periode, on-demand aus den persistierten
    // PayrollSaldo-Zeilen. „buchhaltung" = alle Saldi + Brutto/Netto + IBAN;
    // „gf" = kompakte Übersicht (UTP ohne 13. ML). HR-only Rollen reichen
    // (DefaultPolicy admin/superuser/user); read-only, kein Edit-Lock-Belang.
    [HttpGet("saldo-liste-buchhaltung")]
    [Authorize(Roles = "admin,buchhaltung")]   // Fibu-Bereich: nur Buchhaltung + admin
    public async Task<IActionResult> SaldoListeBuchhaltung(
        [FromQuery] int companyProfileId, [FromQuery] int year, [FromQuery] int month)
    {
        if (!await CanAccessBranchAsync(companyProfileId))
            return StatusCode(403, new { error = "Kein Zugriff auf diese Filiale." });
        if (!await IsDefinitivConfirmedAsync(companyProfileId, year, month))
            return PeriodeNichtAbgeschlossen();
        try
        {
            var pdf = await _saldoListePdf.GenerateBuchhaltungAsync(companyProfileId, year, month);
            return File(pdf, "application/pdf");
        }
        catch (InvalidOperationException ex) { return NotFound(new { error = ex.Message }); }
    }

    [HttpGet("saldo-liste-gf")]
    public async Task<IActionResult> SaldoListeGf(
        [FromQuery] int companyProfileId, [FromQuery] int year, [FromQuery] int month)
    {
        if (!await CanAccessBranchAsync(companyProfileId))
            return StatusCode(403, new { error = "Kein Zugriff auf diese Filiale." });
        if (!await IsDefinitivConfirmedAsync(companyProfileId, year, month))
            return PeriodeNichtAbgeschlossen();
        try
        {
            var pdf = await _saldoListePdf.GenerateGfAsync(companyProfileId, year, month);
            return File(pdf, "application/pdf");
        }
        catch (InvalidOperationException ex) { return NotFound(new { error = ex.Message }); }
    }

    // GET /api/payroll/calculate?employeeId=X&year=Y&month=M&companyProfileId=Z
    // Optional: isCorrection=true → Korrektur-/Sonderlohn (ausgetretene MA).
    [HttpGet("calculate")]
    public async Task<IActionResult> Calculate(
        [FromQuery] int employeeId,
        [FromQuery] int year,
        [FromQuery] int month,
        [FromQuery] int companyProfileId,
        [FromQuery] bool isCorrection = false)
    {
        if (!await CanAccessBranchAsync(companyProfileId))
            return StatusCode(403, new { error = "Kein Zugriff auf diese Filiale." });
        return await _calcEngine.CalculateAsync(employeeId, year, month, companyProfileId, isCorrection);
    }

    // ── Schatten-Basen-Report (Swissdec Schritt 2, Walter-Vorgabe 17.08.2026) ──
    // Rechnet alle MA der Filiale+Periode READ-ONLY durch und aggregiert den
    // «schattenBasen»-Block aus jedem Slip: Flag-Nachrechnung (Lohnzeilen-Codes ×
    // Lohnpositions-Flags) vs. Engine-Basen pro Kategorie (AHV/NBU/KTG/BVG/QST).
    // Reine Diagnose — persistiert nichts (Preview-Grundsatz 20.05.2026).
    [HttpGet("schatten-report")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> SchattenReport(
        [FromQuery] int companyProfileId, [FromQuery] int year, [FromQuery] int month)
    {
        if (!await CanAccessBranchAsync(companyProfileId))
            return StatusCode(403, new { error = "Kein Zugriff auf diese Filiale." });

        var (periodFrom, periodTo) = CalcPeriod(year, month);
        var pFrom = periodFrom.ToDateTime(TimeOnly.MinValue);
        var pTo   = periodTo.ToDateTime(TimeOnly.MaxValue);

        var emps = await (
            from emp in _db.Employments.AsNoTracking()
            join e in _db.Employees.AsNoTracking() on emp.EmployeeId equals e.Id
            where emp.CompanyProfileId == companyProfileId
               && !e.IsPayrollExcluded
               && emp.ContractStartDate <= pTo
               && (emp.ContractEndDate == null || emp.ContractEndDate >= pFrom)
            select new { emp.EmployeeId, e.FirstName, e.LastName,
                         Number = e.EmployeeNumber, emp.EmploymentModel }
        ).ToListAsync();
        var byEmp = emps.GroupBy(x => x.EmployeeId).Select(g => g.First())
            .OrderBy(x => x.FirstName ?? "").ThenBy(x => x.LastName ?? "").ToList();

        var camel = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var rows = new List<object>();
        int okCount = 0, diffCount = 0, fehlerCount = 0;
        foreach (var m in byEmp)
        {
            JsonNode? schatten = null; string? fehler = null;
            try
            {
                var calc = await _calcEngine.CalculateAsync(m.EmployeeId, year, month, companyProfileId);
                if (calc is OkObjectResult okRes && okRes.Value is not null)
                    schatten = JsonNode.Parse(JsonSerializer.Serialize(okRes.Value, camel))?["schattenBasen"];
                else
                    fehler = "Berechnung nicht möglich (kein OK-Resultat).";
            }
            catch (Exception ex) { fehler = ex.Message; }

            bool ok = schatten?["ok"]?.GetValue<bool>() ?? false;
            if (fehler != null) fehlerCount++;
            else if (ok) okCount++;
            else diffCount++;
            rows.Add(new
            {
                employeeId = m.EmployeeId,
                name       = $"{m.FirstName} {m.LastName}".Trim(),
                nummer     = m.Number,
                modell     = m.EmploymentModel,
                ok,
                fehler,
                schatten,
            });
        }

        return Ok(new
        {
            companyProfileId, year, month,
            total = rows.Count, okCount, diffCount, fehlerCount,
            toleranz = SchattenBasenService.Toleranz,
            rows,
        });
    }

    /// <summary>
    /// Kandidaten für Korrekturlohn: MA mit Vertrag in der Filiale, die in
    /// der Periode keinen laufenden Vertrag mehr haben (ausgetreten / inaktiv).
    /// Flaggt zusätzlich offene Zulagen / Depot-Refund.
    /// </summary>
    [HttpGet("correction-candidates")]
    public async Task<IActionResult> CorrectionCandidates(
        [FromQuery] int companyProfileId,
        [FromQuery] int year,
        [FromQuery] int month)
    {
        if (!await CanAccessBranchAsync(companyProfileId))
            return StatusCode(403, new { error = "Kein Zugriff auf diese Filiale." });

        var (periodFrom, periodTo) = CalcPeriod(year, month);
        var periodFromDt = periodFrom.ToDateTime(TimeOnly.MinValue);
        var periodToDt   = periodTo.ToDateTime(TimeOnly.MinValue);
        var periodeStr   = $"{year:D4}-{month:D2}";

        var emps = await _db.Employees.AsNoTracking()
            .Where(e => !e.IsPayrollExcluded)
            .Where(e => e.Employments.Any(x => x.CompanyProfileId == companyProfileId))
            .Select(e => new
            {
                e.Id, e.FirstName, e.LastName, e.EmployeeNumber, e.IsActive, e.ExitDate,
                LastEmp = e.Employments
                    .Where(x => x.CompanyProfileId == companyProfileId)
                    .OrderByDescending(x => x.ContractStartDate)
                    .Select(x => new { x.EmploymentModel, x.ContractStartDate, x.ContractEndDate })
                    .FirstOrDefault(),
                // Noch irgendwo im Unternehmen angestellt (inkl. Filialwechsel /
                // GF anderer Filiale) → KEIN Korrekturlohn-Kandidat.
                // Sonst taucht z.B. Alaa (GF Langenthal, früher Oftringen) in
                // der Oftringen-Liste als «Korr.» auf.
                HasOverlapAnywhere = e.Employments.Any(x =>
                    x.ContractStartDate <= periodToDt
                    && (x.ContractEndDate == null || x.ContractEndDate >= periodFromDt)),
            })
            .ToListAsync();

        // Nur wirklich Ausgetretene: hatten Vertrag in dieser Filiale, und in
        // der Periode nirgends mehr einen laufenden Vertrag.
        var candidates = emps
            .Where(e => e.LastEmp != null && !e.HasOverlapAnywhere)
            .ToList();

        var candIds = candidates.Select(c => c.Id).ToList();
        var zulagenEmpIds = await _db.LohnZulagen.AsNoTracking()
            .Where(z => candIds.Contains(z.EmployeeId) && z.Periode == periodeStr)
            .Select(z => z.EmployeeId)
            .Distinct()
            .ToListAsync();
        var zulagenSet = zulagenEmpIds.ToHashSet();

        var depotEmpIds = await _db.EmployeeUniformDepots.AsNoTracking()
            .Where(d => candIds.Contains(d.EmployeeId)
                     && d.Status == "EINBEHALTEN"
                     && d.Balance > 0
                     && d.ReturnConfirmed == true)
            .Select(d => d.EmployeeId)
            .ToListAsync();
        var depotSet = depotEmpIds.ToHashSet();

        var result = candidates
            .OrderBy(e => e.FirstName ?? "").ThenBy(e => e.LastName ?? "")
            .Select(e => new
            {
                id = e.Id,
                firstName = e.FirstName,
                lastName = e.LastName,
                employeeNumber = e.EmployeeNumber,
                isActive = e.IsActive,
                exitDate = e.ExitDate.HasValue
                    ? DateOnly.FromDateTime(e.ExitDate.Value).ToString("yyyy-MM-dd")
                    : null,
                employmentModel = e.LastEmp!.EmploymentModel,
                hasZulagen = zulagenSet.Contains(e.Id),
                hasPendingDepotRefund = depotSet.Contains(e.Id)
                    && e.ExitDate.HasValue
                    && DateOnly.FromDateTime(e.ExitDate.Value) <= periodTo,
            });

        return Ok(result);
    }

    // GET /api/payroll/sollstunden-report?companyProfileId=X&year=Y&month=M
    // GF-Report (Walter-Vorgabe 19.06.2026): pro FIX/FIX-M/MTP-MA Soll-/Ist-/
    // Absenz-Stunden für den Monat. Nutzt DIESELBE CalculateAsync-Engine wie der
    // Lohnlauf (identische Zahlen) und liest die Stunden-Felder aus dem Resultat.
    [HttpGet("sollstunden-report")]
    public async Task<IActionResult> SollstundenReport(
        [FromQuery] int companyProfileId, [FromQuery] int year, [FromQuery] int month,
        [FromQuery] string? stichtag = null,
        // Optional: nur ein MA (Übersicht-Karte) — gleiche Stichtag-Logik, ohne
        // die ganze Filiale durchzurechnen.
        [FromQuery] int? employeeId = null)
    {
        if (!await CanAccessBranchAsync(companyProfileId))
            return StatusCode(403, new { error = "Kein Zugriff auf diese Filiale." });

        var (periodFrom, periodTo) = CalcPeriod(year, month);
        var pFrom = periodFrom.ToDateTime(TimeOnly.MinValue);
        var pTo   = periodTo.ToDateTime(TimeOnly.MaxValue);

        // Stichtag: bis und mit diesem Tag werden Soll/Gearbeitet/Absenz pro-rata
        // gezeigt (Mid-Month-Fortschritt). Default = heute, geklemmt auf den
        // Monatsbereich. Liegt heute nach Monatsende → ganzer Monat.
        if (!DateOnly.TryParse(stichtag, out var stich))
            stich = DateOnly.FromDateTime(DateTime.Now);
        if (stich < periodFrom) stich = periodFrom;
        if (stich > periodTo)   stich = periodTo;
        int daysInMonth    = periodTo.DayNumber - periodFrom.DayNumber + 1;
        int daysToStichtag = stich.DayNumber - periodFrom.DayNumber + 1;
        decimal dayRatio   = daysInMonth > 0 ? (decimal)daysToStichtag / daysInMonth : 1m;

        // Aktive FIX/FIX-M/MTP-Verträge der Filiale in der Periode (UTP hat keine
        // Sollstunden → bewusst raus). Datums-Vergleich auf DateTime (EF-übersetzbar).
        string[] models = { "FIX", "FIX-M", "MTP" };
        var emps = await (
            from emp in _db.Employments.AsNoTracking()
            join e in _db.Employees.AsNoTracking() on emp.EmployeeId equals e.Id
            where emp.CompanyProfileId == companyProfileId
               && models.Contains(emp.EmploymentModel)
               && emp.ContractStartDate <= pTo
               && (emp.ContractEndDate == null || emp.ContractEndDate >= pFrom)
               && (employeeId == null || emp.EmployeeId == employeeId.Value)
            orderby emp.ContractStartDate descending
            select new { emp.EmployeeId, emp.EmploymentModel, e.FirstName, e.LastName, Number = e.EmployeeNumber,
                         emp.EmploymentPercentage, emp.GuaranteedHoursPerWeek,
                         e.EntryDate, e.ExitDate }
        ).ToListAsync();
        // pro MA den jüngsten passenden Vertrag, dann sortiert nach Vertrag
        // (FIX-M → FIX → MTP) und innerhalb des Vertrags nach Vorname.
        static int ModelRank(string? m) => m == "FIX-M" ? 0 : m == "FIX" ? 1 : m == "MTP" ? 2 : 3;
        var byEmp = emps.GroupBy(x => x.EmployeeId).Select(g => g.First())
            .OrderBy(x => ModelRank(x.EmploymentModel))
            .ThenBy(x => x.FirstName ?? "").ThenBy(x => x.LastName ?? "").ToList();
        var empIds = byEmp.Select(x => x.EmployeeId).ToList();

        // Gearbeitete Stunden bis und mit Stichtag — absolut (Tag+Nacht), analog Engine.
        var workedToStich = (await _db.EmployeeTimeEntries.AsNoTracking()
                .Where(t => empIds.Contains(t.EmployeeId)
                         && t.EntryDate >= periodFrom && t.EntryDate <= stich)
                .ToListAsync())
            .GroupBy(t => t.EmployeeId)
            .ToDictionary(g => g.Key, g => TimeEntryHours.SumAbsolute(g));

        // Absenzen der Periode (für die Tag-Skalierung bis Stichtag).
        var absInPeriod = await _db.Absences.AsNoTracking()
            .Where(a => empIds.Contains(a.EmployeeId)
                     && a.DateFrom <= periodTo && a.DateTo >= periodFrom)
            .ToListAsync();
        var absByEmp = absInPeriod.GroupBy(a => a.EmployeeId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var camel = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        static decimal D(JsonNode? node, string key)
        {
            var v = node?[key];
            if (v is null) return 0m;
            try { return v.GetValue<decimal>(); }
            catch { try { return (decimal)v.GetValue<double>(); } catch { return 0m; } }
        }

        var rows = new List<object>();
        foreach (var e in byEmp)
        {
            var calc = await _calcEngine.CalculateAsync(e.EmployeeId, year, month, companyProfileId);
            if (calc is not OkObjectResult ok || ok.Value is null) continue;
            var node  = JsonNode.Parse(JsonSerializer.Serialize(ok.Value, camel));
            var model = (string?)(node?["employmentModel"]) ?? e.EmploymentModel;
            bool isMtp = model == "MTP";

            // ── Monats-Basiswerte aus der Engine ───────────────────────────
            // Soll (brutto): MTP = vor Absenz-Reduktion (sollStundenVoll);
            //   FIX/FIX-M = sollStunden (deren Soll wird nicht gekürzt).
            // Soll reduziert (netto): das tatsächlich zu leistende Soll nach
            //   Abzug von Ferien/Krank/Unfall — bei MTP = sollStunden (bereits
            //   auf ≥0 gedeckelt), bei FIX/FIX-M = sollStunden (= brutto, da
            //   Krank/Unfall dort über absenzGutschrift gutgeschrieben werden).
            decimal sollBrutto = isMtp ? D(node, "sollStundenVoll") : D(node, "sollStunden");
            decimal sollNetto  = D(node, "sollStunden");
            decimal reduktion  = Math.Max(0, sollBrutto - sollNetto);   // Ferien+Krank+Unfall absorbiert
            decimal absGutMonat = D(node, "absenzGutschrift");          // Zeitgutschrift-Absenzen (Schulung etc.)
            decimal workedMonat = D(node, "workedHours");
            decimal vormonat    = D(node, "vormonatHourSaldo");

            // Absenz-Tag-Anteil bis Stichtag (für die Skalierung).
            decimal absFrac = 0m;
            if (absByEmp.TryGetValue(e.EmployeeId, out var aList))
            {
                int dFull = aList.Sum(a => CountAbsenceDaysInPeriod(a, periodFrom, periodTo));
                int dUpTo = aList.Sum(a => CountAbsenceDaysInPeriod(a, periodFrom, stich));
                absFrac = dFull > 0 ? (decimal)dUpTo / dFull : 0m;
            }

            // ── Monats-Block ───────────────────────────────────────────────
            decimal mtSoll    = Math.Round(sollBrutto, 2);
            decimal mtSollRed = Math.Round(sollNetto, 2);
            decimal mtAbsenz  = Math.Round(absGutMonat + reduktion, 2);   // alle gutgeschriebenen Absenz-Std
            decimal mtGearb   = Math.Round(workedMonat, 2);
            decimal mtTotal   = Math.Round(mtGearb + mtAbsenz, 2);
            decimal mtSaldo   = Math.Round(vormonat + (mtGearb + absGutMonat - sollNetto), 2);

            // ── Stichtag-Block (anteilig: Soll pro Kalendertag, Absenz pro Absenz-Tag) ──
            decimal stGearb     = workedToStich.TryGetValue(e.EmployeeId, out var w) ? Math.Round(w, 2) : 0m;
            decimal stSollBrutto = sollBrutto * dayRatio;
            decimal stReduktion  = reduktion * absFrac;
            decimal stSollRedRaw = stSollBrutto - stReduktion;
            decimal stAbsGut     = absGutMonat * absFrac;
            decimal stSoll    = Math.Round(stSollBrutto, 2);
            decimal stSollRed = Math.Round(stSollRedRaw, 2);
            decimal stAbsenz  = Math.Round(stAbsGut + stReduktion, 2);
            decimal stTotal   = Math.Round(stGearb + stAbsenz, 2);
            decimal stSaldo   = Math.Round(vormonat + (stGearb + stAbsGut - stSollRedRaw), 2);

            decimal saldoVor = Math.Round(vormonat, 2);   // Übertrag Vormonat (gleich für beide Blöcke)

            // Ein-/Austritt in dieser Periode → Namens-Markierung (grün/rot).
            // Bewusst am MITARBEITER (EntryDate/ExitDate), NICHT am Vertrag —
            // ein Vertragswechsel (alter Vertrag endet, neuer beginnt) ist KEIN
            // Austritt und darf nicht rot markiert werden.
            bool eintritt = e.EntryDate.HasValue
                && DateOnly.FromDateTime(e.EntryDate.Value) >= periodFrom
                && DateOnly.FromDateTime(e.EntryDate.Value) <= periodTo;
            bool austritt = e.ExitDate.HasValue
                && DateOnly.FromDateTime(e.ExitDate.Value) >= periodFrom
                && DateOnly.FromDateTime(e.ExitDate.Value) <= periodTo;

            rows.Add(new
            {
                employeeId = e.EmployeeId,
                number     = e.Number,
                name       = $"{e.FirstName} {e.LastName}".Trim(),
                model,
                pensum          = e.EmploymentPercentage,      // FIX/FIX-M: Stellenprozent
                guaranteedHours = e.GuaranteedHoursPerWeek,    // MTP: garantierte Wochenstunden
                eintritt, austritt,
                // Stichtag-Block
                stSaldoVor = saldoVor, stSoll, stSollRed, stAbsenz, stGearb, stTotal, stSaldo,
                // Monats-Block
                mtSaldoVor = saldoVor, mtSoll, mtSollRed, mtAbsenz, mtGearb, mtTotal, mtSaldo
            });
        }

        return Ok(new
        {
            periodFrom = periodFrom.ToString("yyyy-MM-dd"),
            periodTo   = periodTo.ToString("yyyy-MM-dd"),
            stichtag   = stich.ToString("yyyy-MM-dd"),
            daysToStichtag,
            daysInMonth,
            count      = rows.Count,
            rows
        });
    }

    // GET /api/payroll/sollstunden-report/pdf — gleiche Daten wie JSON, A4 quer
    // (Walter 03.08.2026).
    [HttpGet("sollstunden-report/pdf")]
    public async Task<IActionResult> SollstundenReportPdf(
        [FromQuery] int companyProfileId, [FromQuery] int year, [FromQuery] int month,
        [FromQuery] string? stichtag = null)
    {
        var json = await SollstundenReport(companyProfileId, year, month, stichtag);
        if (json is not OkObjectResult ok || ok.Value is null) return json;

        var camel = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var payload = JsonSerializer.SerializeToElement(ok.Value, camel);
        var rowList = payload.TryGetProperty("rows", out var rowsEl)
            ? JsonSerializer.Deserialize<List<AuswertungenReportPdfService.SollRow>>(rowsEl.GetRawText(), camel)
              ?? new List<AuswertungenReportPdfService.SollRow>()
            : new List<AuswertungenReportPdfService.SollRow>();

        var branch = await BranchLabelAsync(companyProfileId);
        var pdf = _auswertungenPdf.GenerateSollstunden(
            branch,
            payload.GetProperty("periodFrom").GetString() ?? "",
            payload.GetProperty("periodTo").GetString() ?? "",
            payload.GetProperty("stichtag").GetString() ?? "",
            payload.GetProperty("daysToStichtag").GetInt32(),
            payload.GetProperty("daysInMonth").GetInt32(),
            rowList);

        var fname = $"Sollstunden_{year}-{month:D2}.pdf";
        return File(pdf, "application/pdf", fname);
    }

    // GET /api/payroll/ferien-report?companyProfileId=X&year=Y&month=M
    // Ferien-Anspruch pro MA in TAGEN, aufgelaufen von Januar bis und mit Stichtag-
    // Monat M. Walter-Vorgabe 20.06.2026: es gibt noch keine Ferien-Saldi — also
    // rechnen wir den Anspruch STANDALONE (ohne Lohnlauf): Ferienwochen × 7 / 12
    // pro angestelltem Monat. 6 Wochen ab dem Monat NACH dem konfigurierten
    // Geburtstag (VacationSixWeeksFromAge). Ferienkürzung bei langer Krankheit
    // über FerienKuerzungService. ALLE Modelle inkl. UTP/MTP rechnen in Tagen —
    // auch UTP bekommt Ferien als Tage (kein Feriengeld-Auszahlen). Bezug = Tage
    // aus FERIEN-Absenzen. Saldo = Anspruch − Kürzung − Bezug.
    [HttpGet("ferien-report")]
    public async Task<IActionResult> FerienReport(
        [FromQuery] int companyProfileId, [FromQuery] int year, [FromQuery] int month)
    {
        if (!await CanAccessBranchAsync(companyProfileId))
            return StatusCode(403, new { error = "Kein Zugriff auf diese Filiale." });

        if (month < 1) month = 1;
        if (month > 12) month = 12;

        var yearStart = new DateTime(year, 1, 1);
        var yearEnd   = new DateTime(year, 12, 31);

        // Alle Verträge der Filiale, die irgendwann im Jahr aktiv sind.
        var emps = await (
            from emp in _db.Employments.AsNoTracking()
            join e in _db.Employees.AsNoTracking() on emp.EmployeeId equals e.Id
            where emp.CompanyProfileId == companyProfileId
               && emp.ContractStartDate <= yearEnd
               && (emp.ContractEndDate == null || emp.ContractEndDate >= yearStart)
            orderby emp.ContractStartDate descending
            select new { emp.EmployeeId, emp.EmploymentModel, e.FirstName, e.LastName, Number = e.EmployeeNumber,
                         emp.EmploymentPercentage, emp.GuaranteedHoursPerWeek, e.EntryDate, e.ExitDate, e.DateOfBirth,
                         e.NightWorkExamValidUntil, e.NightWorkExamDokumentId, e.NightWorkAusnahmeDokumentId }
        ).ToListAsync();

        static int ModelRank2(string? m) => m == "FIX-M" ? 0 : m == "FIX" ? 1 : m == "MTP" ? 2 : 3;
        var byEmp = emps.GroupBy(x => x.EmployeeId).Select(g => g.First())
            .OrderBy(x => ModelRank2(x.EmploymentModel))
            .ThenBy(x => x.FirstName ?? "").ThenBy(x => x.LastName ?? "").ToList();
        var empIds = byEmp.Select(x => x.EmployeeId).ToList();

        // Filial-Config: Ferien-% (5/6-Wochen) + Alters-Schwelle für 6 Wochen.
        var company = await _db.CompanyProfiles.AsNoTracking()
            .Where(c => c.Id == companyProfileId)
            .Select(c => new { c.DefaultVacationPercent5Weeks, c.DefaultVacationPercent6Weeks, c.VacationSixWeeksFromAge })
            .FirstOrDefaultAsync();
        decimal basePct = company?.DefaultVacationPercent5Weeks ?? 10.65m;
        decimal sixPct  = company?.DefaultVacationPercent6Weeks ?? 13.04m;
        int sixFromAge  = company?.VacationSixWeeksFromAge ?? 50;

        var yearStartD = new DateOnly(year, 1, 1);
        var stichEnd   = new DateOnly(year, month, DateTime.DaysInMonth(year, month));

        // Bezogene Ferien + Feiertage + Nacht-Kompensation im Bereich Jan..Stichtag.
        var ferAbs = await _db.Absences.AsNoTracking()
            .Where(a => empIds.Contains(a.EmployeeId)
                     && (a.AbsenceType == "FERIEN" || a.AbsenceType == "FEIERTAG" || a.AbsenceType == "NACHT_KOMP")
                     && a.DateFrom <= stichEnd && a.DateTo >= yearStartD)
            .ToListAsync();
        var ferAbsByEmp = ferAbs.GroupBy(a => a.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());

        // Nachtstunden aus den Stempelzeiten (Jan..Stichtag) — für ALLE Modelle.
        // Pro MA filtern wir unten ggf. ab Vortrags-Monat (904), damit Mirus-
        // Schlussaldo und Stempelzeiten nicht doppelt zählen (Walter 02.08.2026).
        var nightEntries = await _db.EmployeeTimeEntries.AsNoTracking()
            .Where(t => empIds.Contains(t.EmployeeId)
                     && t.EntryDate >= yearStartD && t.EntryDate <= stichEnd
                     && (t.NightHours ?? 0m) != 0m)
            .Select(t => new { t.EmployeeId, t.EntryDate, Night = t.NightHours ?? 0m })
            .ToListAsync();
        var nightEntriesByEmp = nightEntries
            .GroupBy(t => t.EmployeeId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Vortrag Nacht-Saldo (904) — Migrations-Eröffnung aus Monatsblatt.
        var vortrag904Raw = await _db.LohnZulagen.AsNoTracking()
            .Where(z => empIds.Contains(z.EmployeeId)
                     && z.Lohnposition != null
                     && z.Lohnposition.Code == "904"
                     && z.Lohnposition.Kategorie == "Saldo-Vortrag")
            .Select(z => new { z.EmployeeId, z.Periode, z.Betrag })
            .ToListAsync();
        // Pro MA: jüngster Vortrag mit Periode ≤ Stichtag-Monat.
        var stichPeriode = $"{year}-{month:D2}";
        var vortrag904ByEmp = vortrag904Raw
            .Where(v => !string.IsNullOrEmpty(v.Periode) && string.CompareOrdinal(v.Periode, stichPeriode) <= 0)
            .GroupBy(v => v.EmployeeId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.Periode).First());

        // Anzahl gearbeitete NÄCHTE über die letzten 12 Monate ab Stichtag
        // (Walter-Vorgabe 20.06.2026, ArG-Nachtarbeit-Kontrolle): rollendes Fenster,
        // Nacht = Kalendertag mit Nachtstunden > 0. Bei < 12 Datenmonaten hochrechnen:
        // Nächte / Datenmonate × 12.
        var rollStart = new DateOnly(year, month, 1).AddMonths(-11);
        var teRoll = await _db.EmployeeTimeEntries.AsNoTracking()
            .Where(t => empIds.Contains(t.EmployeeId) && t.EntryDate >= rollStart && t.EntryDate <= stichEnd)
            .Select(t => new { t.EmployeeId, t.EntryDate, t.NightHours })
            .ToListAsync();
        var teRollByEmp = teRoll.GroupBy(t => t.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());

        // Wochen-Satz für einen Monat: 6 Wochen ab dem Monat NACH dem Geburtstag,
        // an dem die Alters-Schwelle erreicht wird (Walter-Vorgabe 20.06.2026:
        // „im Folgemonat nach dem 50. Geburtstag"); sonst der Basissatz.
        int WeeksForMonth(DateOnly? dob, DateOnly monthStart)
        {
            decimal pct = basePct;
            if (dob.HasValue)
            {
                var schwelle = dob.Value.AddYears(sixFromAge);            // z.B. 50. Geburtstag
                var bumpFrom = new DateOnly(schwelle.Year, schwelle.Month, 1).AddMonths(1); // Folgemonat
                if (monthStart >= bumpFrom && sixPct > pct) pct = sixPct;
            }
            return pct >= 12.5m ? 6 : 5;
        }

        var rows = new List<object>();
        int nachtWarnTotal = 0;
        foreach (var e in byEmp)
        {
            DateOnly? entry = e.EntryDate.HasValue ? DateOnly.FromDateTime(e.EntryDate.Value) : null;
            DateOnly? exit  = e.ExitDate.HasValue  ? DateOnly.FromDateTime(e.ExitDate.Value)  : null;
            DateOnly? dob   = e.DateOfBirth.HasValue ? DateOnly.FromDateTime(e.DateOfBirth.Value) : null;

            // MA, die VOR dem selektierten Monat ausgetreten sind, nicht mehr
            // zeigen (beim Austritt werden die Ferien ausbezahlt). Ebenso MA, die
            // bis zum Stichtag noch nicht eingetreten sind.
            var selMonthStart = new DateOnly(year, month, 1);
            if (exit.HasValue && exit.Value < selMonthStart) continue;
            if (entry.HasValue && entry.Value > stichEnd) continue;

            bool isFix = e.EmploymentModel == "FIX" || e.EmploymentModel == "FIX-M";

            // ── Anspruch: Wochen × 7 / 12 pro angestelltem Monat (Jan..Stichtag) ──
            // Eintritts-/Austrittsmonat TAG-GENAU pro-rata (anteilig nach den im
            // Monat angestellten Tagen); volle Monate zählen voll.
            // Feiertage (nur FIX/FIX-M): +0.5 Tage/Monat, gleich pro-rata.
            decimal anspruch = 0m;
            decimal feiertagAnspruch = 0m;
            int weeksNow = WeeksForMonth(dob, new DateOnly(year, month, 1));
            for (int m = 1; m <= month; m++)
            {
                var mStart = new DateOnly(year, m, 1);
                var mEnd   = new DateOnly(year, m, DateTime.DaysInMonth(year, m));
                // angestelltes Fenster innerhalb des Monats
                var effStart = (entry.HasValue && entry.Value > mStart) ? entry.Value : mStart;
                var effEnd   = (exit.HasValue  && exit.Value  < mEnd)   ? exit.Value  : mEnd;
                if (effEnd < effStart) continue;   // diesen Monat nicht angestellt
                int presentDays = effEnd.DayNumber - effStart.DayNumber + 1;
                int monthDays   = DateTime.DaysInMonth(year, m);
                decimal frac    = (decimal)presentDays / monthDays;
                int weeks = WeeksForMonth(dob, mStart);
                anspruch += weeks * 7m / 12m * frac;
                if (isFix) feiertagAnspruch += 0.5m * frac;
            }

            // ── Nacht-Vortrag (904) + Fenster ab Vortrags-Monat ───────────
            decimal nachtVortrag = 0m;
            string? nachtVortragPeriode = null;
            DateOnly nachtFrom = yearStartD;
            if (vortrag904ByEmp.TryGetValue(e.EmployeeId, out var v904))
            {
                var basis = ResolveNachtReportBasis(yearStartD, stichEnd, v904.Periode, v904.Betrag);
                nachtFrom = basis.NightFrom;
                nachtVortrag = basis.Vortrag;
                nachtVortragPeriode = v904.Periode;
            }

            // ── Bezug: FERIEN-Tage + Feiertag-Tage + Nacht-Komp-Stunden ──
            decimal bezug = 0m;
            decimal feiertagBezug = 0m;
            decimal nachtKomp = 0m;
            if (ferAbsByEmp.TryGetValue(e.EmployeeId, out var fa))
            {
                bezug = fa.Where(a => a.AbsenceType == "FERIEN")
                          .Sum(a => CountAbsenceDaysInPeriod(a, yearStartD, stichEnd));
                feiertagBezug = fa.Where(a => a.AbsenceType == "FEIERTAG")
                          .Sum(a => CountAbsenceDaysInPeriod(a, yearStartD, stichEnd)
                                    * ((a.Prozent > 0 ? a.Prozent : 100m) / 100m));
                // Nacht-Komp nur im Fenster ab Vortrag (sonst Doppelzählung).
                nachtKomp = fa.Where(a => a.AbsenceType == "NACHT_KOMP")
                          .Sum(a => ScaleAbsenceHoursToPeriod(a, nachtFrom, stichEnd));
            }

            // ── Nacht-Saldo (Stunden, alle Modelle inkl. FLEX):
            //    Vortrag + Nachtstunden×10% − Nacht-Kompensation. ──
            decimal nachtStd = 0m;
            if (nightEntriesByEmp.TryGetValue(e.EmployeeId, out var nEnts))
                nachtStd = nEnts.Where(t => t.EntryDate >= nachtFrom && t.EntryDate <= stichEnd)
                                .Sum(t => t.Night);
            decimal nachtZuschlag = Math.Round(nachtStd * 0.10m, 2);
            decimal nachtSaldo    = Math.Round(nachtVortrag + nachtZuschlag - nachtKomp, 2);

            // ── Anzahl Nächte (rollende 12 Monate, ArG-Kontrolle) ──
            int naechteReal = 0, datenMonate = 0;
            if (teRollByEmp.TryGetValue(e.EmployeeId, out var teR))
            {
                naechteReal = teR.Where(x => (x.NightHours ?? 0m) > 0m)
                                 .Select(x => x.EntryDate).Distinct().Count();
                datenMonate = teR.Select(x => x.EntryDate.Year * 12 + x.EntryDate.Month).Distinct().Count();
            }
            // Hochrechnung aufs Jahr, wenn weniger als 12 Datenmonate vorliegen.
            int naechteJahr = datenMonate >= 12 ? naechteReal
                            : datenMonate > 0 ? (int)Math.Round((double)naechteReal * 12 / datenMonate)
                            : 0;

            // Compliance-Warnung (ArGV1 Art. 30, Walter-Vorgabe 22.06.2026):
            // > 18 Nächte in einem rollierenden 6-Wochen-Fenster (42 Tage) UND
            // Nachweise unvollständig (Arztzeugnis/Verzicht UND Ausnahmeregelung).
            var nachtDates = teRollByEmp.TryGetValue(e.EmployeeId, out var teRollNd)
                ? teRollNd.Where(x => (x.NightHours ?? 0m) > 0m).Select(x => x.EntryDate)
                : Enumerable.Empty<DateOnly>();
            var nwEval = NightWorkComplianceService.Evaluate(nachtDates, stichEnd);
            bool nachweiseFehlen = !(e.NightWorkExamDokumentId.HasValue && e.NightWorkAusnahmeDokumentId.HasValue);
            bool nachtWarn = nwEval.RequiresDocuments && nachweiseFehlen;
            string? nachtWarnReason = !nachtWarn ? null
                : (!e.NightWorkExamDokumentId.HasValue && !e.NightWorkAusnahmeDokumentId.HasValue) ? "Arztzeugnis/Verzicht und Ausnahmeregelung fehlen"
                : (!e.NightWorkExamDokumentId.HasValue) ? "Arztzeugnis/Verzicht fehlt"
                : "Ausnahmeregelung fehlt";
            // examGueltig bleibt für Rückwärtskompatibilität (nicht mehr Warnkriterium).
            bool examGueltig = e.NightWorkExamDokumentId.HasValue
                            && e.NightWorkExamValidUntil.HasValue
                            && DateOnly.FromDateTime(e.NightWorkExamValidUntil.Value) >= stichEnd;
            if (nachtWarn) nachtWarnTotal++;

            // ── Ferienkürzung bei langer Krankheit (Art. 329b OR) ──
            // Eigener, schlanker Service (kein Lohnlauf): 1/12 pro vollem Monat
            // über Schwellwert × Jahres-Ferientage (Wochen × 7).
            var kuerz = await _ferienKuerzung.CalculateAsync(e.EmployeeId, stichEnd);
            decimal kuerzungTage = kuerz.HasKuerzungVorschlag
                ? Math.Round(kuerz.TotalKuerzung12tel * (weeksNow * 7m) / 12m, 2)
                : 0m;

            decimal saldo = anspruch - kuerzungTage - bezug;

            // Farbliche Markierung NUR wenn Ein-/Austritt im selektierten Monat liegt.
            bool eintritt = entry.HasValue && entry.Value.Year == year && entry.Value.Month == month;
            bool austritt = exit.HasValue  && exit.Value.Year  == year && exit.Value.Month  == month;

            rows.Add(new
            {
                employeeId      = e.EmployeeId,
                number          = e.Number,
                name            = $"{e.FirstName} {e.LastName}".Trim(),
                model           = e.EmploymentModel,
                pensum          = e.EmploymentPercentage,
                guaranteedHours = e.GuaranteedHoursPerWeek,
                vacationWeeks   = weeksNow,
                anspruchTage    = Math.Round(anspruch, 2),
                kuerzungTage    = Math.Round(kuerzungTage, 2),
                bezugTage       = Math.Round(bezug, 2),
                saldoTage       = Math.Round(saldo, 2),
                // Feiertage nur FIX/FIX-M (sonst null → „–"; MTP/UTP ausbezahlt).
                feiertagAnspruch = isFix ? (decimal?)Math.Round(feiertagAnspruch, 2) : null,
                feiertagBezug    = isFix ? (decimal?)Math.Round(feiertagBezug, 2) : null,
                feiertagSaldo    = isFix ? (decimal?)Math.Round(feiertagAnspruch - feiertagBezug, 2) : null,
                // Nacht-Saldo in Stunden (alle Modelle inkl. FLEX).
                nachtVortrag        = Math.Round(nachtVortrag, 2),
                nachtVortragPeriode = nachtVortragPeriode,
                nachtStunden        = Math.Round(nachtStd, 2),
                nachtZuschlag       = nachtZuschlag,
                nachtKomp           = Math.Round(nachtKomp, 2),
                nachtSaldo          = nachtSaldo,
                // Anzahl Nächte (rollende 12 Monate) — nur noch Info, NICHT mehr Warnkriterium.
                naechteJahr   = naechteJahr,
                naechteReal   = naechteReal,
                datenMonate   = datenMonate,
                // NEUE 6-Wochen-Regel (ArGV1 Art. 30): > 18 Nächte in 42 Tagen.
                maxNaechte6Wochen = nwEval.MaxNightsInSixWeeks,
                nachtWindowFrom   = nwEval.WindowFrom?.ToString("yyyy-MM-dd"),
                nachtWindowTo     = nwEval.WindowTo?.ToString("yyyy-MM-dd"),
                nachtWarn     = nachtWarn,                       // >18 Nächte/6 Wochen UND Nachweise fehlen
                nachtWarnReason = nachtWarnReason,
                examGueltig   = examGueltig,                     // (Kompatibilität, nicht mehr Warnkriterium)
                nachtExamBis  = e.NightWorkExamValidUntil,       // gültig bis (für Tooltip)
                nachtExamDoc  = e.NightWorkExamDokumentId.HasValue,
                eintritt, austritt
            });
        }

        return Ok(new
        {
            year,
            month,
            nachtRollFrom = rollStart.ToString("yyyy-MM-dd"),   // Start des 12-Monats-Datenfensters
            nachtWarnTotal,                                     // MA mit >18 Nächten/6 Wochen ohne vollständige Nachweise
            count = rows.Count,
            rows
        });
    }

    // GET /api/payroll/ferien-report/pdf — A4 quer (Walter 03.08.2026).
    [HttpGet("ferien-report/pdf")]
    public async Task<IActionResult> FerienReportPdf(
        [FromQuery] int companyProfileId, [FromQuery] int year, [FromQuery] int month)
    {
        var json = await FerienReport(companyProfileId, year, month);
        if (json is not OkObjectResult ok || ok.Value is null) return json;

        var camel = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var payload = JsonSerializer.SerializeToElement(ok.Value, camel);
        var rowList = payload.TryGetProperty("rows", out var rowsEl)
            ? JsonSerializer.Deserialize<List<AuswertungenReportPdfService.FerienRow>>(rowsEl.GetRawText(), camel)
              ?? new List<AuswertungenReportPdfService.FerienRow>()
            : new List<AuswertungenReportPdfService.FerienRow>();

        var branch = await BranchLabelAsync(companyProfileId);
        var pdf = _auswertungenPdf.GenerateFerien(
            branch, year, month,
            payload.TryGetProperty("nachtWarnTotal", out var nw) ? nw.GetInt32() : 0,
            rowList);

        return File(pdf, "application/pdf", $"Ferien_Feiertage_Nacht_{year}-{month:D2}.pdf");
    }

    private async Task<string> BranchLabelAsync(int companyProfileId)
    {
        var b = await _db.CompanyProfiles.AsNoTracking()
            .Where(c => c.Id == companyProfileId)
            .Select(c => new { c.RestaurantCode, c.City, c.BranchName, c.CompanyName })
            .FirstOrDefaultAsync();
        if (b is null) return $"Filiale {companyProfileId}";
        var ort = !string.IsNullOrWhiteSpace(b.City) ? b.City
            : (b.BranchName ?? b.CompanyName ?? "");
        return string.IsNullOrWhiteSpace(b.RestaurantCode)
            ? ort
            : $"{b.RestaurantCode} — {ort}".Trim(' ', '—');
    }

    // Diagnose: zeigt was die QST-Engine-Logik intern ENTSCHEIDEN würde, ohne
    // den vollen Lohnzettel zu rechnen. Walter-Vorgabe 26.05.2026.
    // Walter 09.06.2026: admin/superuser-only — reines Diagnose-Werkzeug, hat
    // keine companyProfileId-Eingrenzung und liefert MA-Steuerdaten roh.
    [Authorize(Roles = "admin,superuser")]
    [HttpGet("qst-diag")]
    public async Task<IActionResult> QstDiag(
        [FromQuery] int employeeId,
        [FromQuery] int year,
        [FromQuery] int month)
    {
        var emp = await _db.Employees
            .Include(e => e.NationalityRef)
            .FirstOrDefaultAsync(e => e.Id == employeeId);
        if (emp == null) return NotFound();

        var periodFrom = new DateOnly(year, month, 1);
        bool isSchweizer = string.Equals(emp.NationalityRef?.Code, "CH",
            StringComparison.OrdinalIgnoreCase);
        bool bereitsBefreit = emp.QuellensteuerBefreitAb.HasValue
            && emp.QuellensteuerBefreitAb.Value <= periodFrom;
        bool behoerdenBefreit = emp.QstBefreitDurchBehoerde
            && emp.QstBefreiungDokumentId.HasValue
            && (!emp.QstBefreiungGueltigAb.HasValue
                || emp.QstBefreiungGueltigAb.Value <= periodFrom)
            && (!emp.QstBefreiungGueltigBis.HasValue
                || emp.QstBefreiungGueltigBis.Value >= periodFrom);

        // Diagnostik analog Engine: Überlappung mit der Periode (nicht nur am 1.)
        var periodTo = periodFrom.AddMonths(1).AddDays(-1);
        var qst = await _db.EmployeeQuellensteuer
            .Where(q => q.EmployeeId == employeeId
                     && q.ValidFrom <= periodTo
                     && (q.ValidTo == null || q.ValidTo >= periodFrom))
            .OrderByDescending(q => q.ValidFrom)
            .FirstOrDefaultAsync();

        bool isQuellensteuer = !behoerdenBefreit
            && ((!isSchweizer && !bereitsBefreit) || qst != null);

        return Ok(new {
            employeeId,
            year, month, periodFrom,
            nationality = emp.NationalityRef?.Code,
            isSchweizer,
            quellensteuerBefreitAb = emp.QuellensteuerBefreitAb,
            bereitsBefreit,
            qstBefreitDurchBehoerde = emp.QstBefreitDurchBehoerde,
            behoerdenBefreit,
            qstEntry = qst == null ? null : new {
                qst.Id, qst.ValidFrom, qst.ValidTo,
                qst.Steuerkanton, qst.TarifCode, qst.QstCode,
                qst.AnzahlKinder, qst.Kirchensteuer, qst.Prozentsatz
            },
            isQuellensteuer,
            engineWillBerechnen = isQuellensteuer && qst != null,
            buildVersion = "2026-08-02-qst-period-overlap"   // Walter — wenn dieser Text im JSON steht, läuft der neue Code
        });
    }

    // /api/payroll/save — entfernt (Walter-Vorgabe 09.06.2026).
    //
    // Dieser Endpunkt akzeptierte HourSaldo/GrossAmount/NetAmount/13.ML/Ferien-
    // Geld u.v.m. UNVERIFIZIERT direkt aus dem Request-Body. Damit konnte ein
    // angemeldeter User für seine Filiale beliebige Lohn-/Saldo-Beträge in die
    // DB schreiben, ohne dass der Server nachrechnete. Im Frontend wurde der
    // Endpunkt nicht (mehr) genutzt — der einzig legitime Schreibpfad zu
    // PayrollSaldo läuft über /api/payroll/confirm, der die Beträge intern via
    // CalculateAsync server-autoritativ regeneriert (vgl. Walter-Vorgabe
    // 20.05.2026 „Lohn-Beträge server-autoritativ"). Das DTO `SaveSaldoDto` ist
    // ebenfalls entfernt; falls je wieder eine „Zwischenstand"-API gebraucht
    // wird, muss sie genauso wie Confirm intern rechnen und darf nur GF-
    // Entscheidungen (z.B. ApplyFerienKuerzung) vom Client annehmen.

    // GET /api/payroll/saldo?employeeId=X&year=Y&month=M&companyProfileId=Z
    //
    // CompanyProfileId ist Pflicht: ein Mitarbeiter kann in mehreren Filialen
    // (CompanyProfiles) einen Saldo haben. Ohne diesen Filter würde das
    // FirstOrDefault einen zufälligen davon zurückliefern — und der Reopen-
    // Button im Frontend wäre dann für den falschen Datensatz aktiv.
    // GET /api/payroll/pdf?employeeId=X&year=Y&month=M&companyProfileId=Z
    // Liefert die Lohnabrechnung als PDF (gleicher Look wie der Vertrag).
    // Internally: ruft Calculate (gleiches Lohn-Result) und übergibt es an
    // PayrollPdfService → A4-PDF mit Banner.
    [HttpGet("pdf")]
    public async Task<IActionResult> GetPdf(
        [FromQuery] int employeeId,
        [FromQuery] int year,
        [FromQuery] int month,
        [FromQuery] int companyProfileId)
    {
        if (!await CanAccessBranchAsync(companyProfileId))
            return StatusCode(403, new { error = "Kein Zugriff auf diese Filiale." });
        // Bei abgeschlossenen / provisorisch abgeschlossenen Perioden: aus dem
        // eingefrorenen Snapshot regenerieren (deterministische Daten + Datum).
        // Sonst Live-Berechnung wie bisher.
        var periode = await _db.PayrollPerioden
            .FirstOrDefaultAsync(p => p.CompanyProfileId == companyProfileId
                                   && p.Year == year
                                   && p.Month == month);
        PayrollSnapshot? snapshot = null;
        if (periode != null)
        {
            snapshot = await _db.PayrollSnapshots
                .FirstOrDefaultAsync(s => s.PayrollPeriodeId == periode.Id
                                       && s.EmployeeId == employeeId);
        }

        System.Text.Json.JsonElement json;
        if (snapshot != null && periode != null
            && (periode.Status == "abgeschlossen" || periode.Status == "provisorisch_abgeschlossen"))
        {
            // Aus Snapshot — eingefrorene Werte. printDate wird auf das
            // Periode-Abschluss-Datum gesetzt damit alle Belege einer Periode
            // dasselbe „Erstellt am"-Datum tragen.
            var node = System.Text.Json.Nodes.JsonNode.Parse(snapshot.SlipJson)!.AsObject();
            DateTime? frozenDate = periode.Status == "abgeschlossen"
                ? periode.AbgeschlossenAm
                : periode.ProvisorischAbgeschlossenAm;
            if (frozenDate.HasValue)
            {
                node["printDate"] = frozenDate.Value.ToLocalTime().ToString("dd.MM.yyyy");
            }
            json = System.Text.Json.JsonSerializer.SerializeToElement(node);
        }
        else
        {
            // Offen oder kein Snapshot → Live-Berechnung wie bisher
            var calcResult = await _calcEngine.CalculateAsync(employeeId, year, month, companyProfileId);
            if (calcResult is not OkObjectResult ok || ok.Value is null)
                return calcResult;  // Fehler oder NotFound durchreichen
            json = System.Text.Json.JsonSerializer.SerializeToElement(ok.Value);
        }

        var pdf = _payrollPdf.Generate(json);

        var employee = await _db.Employees.FindAsync(employeeId);
        var name = employee != null
            ? $"{employee.FirstName}_{employee.LastName}".Replace(" ", "_")
            : $"Mitarbeiter_{employeeId}";
        var fileName = $"Lohnabrechnung_{name}_{year}-{month:D2}.pdf";
        return File(pdf, "application/pdf", fileName);
    }

    // GET /api/payroll/stundenkontrolle-pdf?employeeId&year&month&companyProfileId
    // Monatsblatt zur Stundenkontrolle + Unterschrift (Walter 01.08.2026).
    // Geht beim Definitiv-Versand zusammen mit dem Lohnzettel ins MA-Postfach.
    [HttpGet("stundenkontrolle-pdf")]
    public async Task<IActionResult> GetStundenkontrollePdf(
        [FromQuery] int employeeId,
        [FromQuery] int year,
        [FromQuery] int month,
        [FromQuery] int companyProfileId)
    {
        if (!await CanAccessBranchAsync(companyProfileId))
            return StatusCode(403, new { error = "Kein Zugriff auf diese Filiale." });

        var periode = await _db.PayrollPerioden
            .FirstOrDefaultAsync(p => p.CompanyProfileId == companyProfileId
                                   && p.Year == year
                                   && p.Month == month);

        byte[] pdf;
        try
        {
            var slip = await LoadStundenkontrolleSlipAsync(employeeId, year, month, companyProfileId, periode);
            pdf = await _stundenkontrollePdf.GenerateAsync(employeeId, year, month, companyProfileId, slip);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }

        var employee = await _db.Employees.FindAsync(employeeId);
        var name = employee != null
            ? $"{employee.FirstName}_{employee.LastName}".Replace(" ", "_")
            : $"Mitarbeiter_{employeeId}";
        var fileName = $"Stundenkontrolle_{name}_{year}-{month:D2}.pdf";
        return File(pdf, "application/pdf", fileName);
    }

    // Slip-Quelle fürs Stundenkontrollblatt: eingefrorene Periode → Snapshot-
    // SlipJson (mit Abschluss-Datum als printDate), sonst Live-Rechnung.
    // Ohne Slip wird das Blatt trotzdem aus Stempelzeiten/Saldi erzeugt.
    private async Task<System.Text.Json.JsonElement?> LoadStundenkontrolleSlipAsync(
        int employeeId, int year, int month, int companyProfileId, PayrollPeriode? periode)
    {
        PayrollSnapshot? snapshot = null;
        if (periode != null)
        {
            snapshot = await _db.PayrollSnapshots
                .FirstOrDefaultAsync(s => s.PayrollPeriodeId == periode.Id
                                       && s.EmployeeId == employeeId);
        }

        if (snapshot != null && periode != null
            && (periode.Status == "abgeschlossen" || periode.Status == "provisorisch_abgeschlossen"))
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(snapshot.SlipJson)!.AsObject();
            DateTime? frozenDate = periode.Status == "abgeschlossen"
                ? periode.AbgeschlossenAm
                : periode.ProvisorischAbgeschlossenAm;
            if (frozenDate.HasValue)
                node["printDate"] = frozenDate.Value.ToLocalTime().ToString("dd.MM.yyyy");
            return System.Text.Json.JsonSerializer.SerializeToElement(node);
        }

        var calcResult = await _calcEngine.CalculateAsync(employeeId, year, month, companyProfileId);
        if (calcResult is OkObjectResult ok && ok.Value is not null)
            return System.Text.Json.JsonSerializer.SerializeToElement(ok.Value);
        return null;
    }

    // GET /api/payroll/stundenkontrolle-alle-pdf?companyProfileId&year&month
    // Stundenkontrollblätter ALLER MA des Lohnlaufs in EINEM PDF (Walter
    // 04.08.2026) — gleiche MA-Auswahl wie die Lohnlauf-Liste: Vertrag
    // überlappt die Periode, keine Phantom-MA. Sortierung nach Vorname
    // (Walter-Konvention). Pro MA die identische Einzelblatt-Logik, danach
    // iText-Merge.
    [HttpGet("stundenkontrolle-alle-pdf")]
    public async Task<IActionResult> GetStundenkontrolleAllePdf(
        [FromQuery] int companyProfileId,
        [FromQuery] int year,
        [FromQuery] int month)
    {
        if (!await CanAccessBranchAsync(companyProfileId))
            return StatusCode(403, new { error = "Kein Zugriff auf diese Filiale." });

        var (pFrom, pTo) = PayrollCalculations.CalcPeriod(year, month);
        var pFromDt = pFrom.ToDateTime(TimeOnly.MinValue);
        var pToDt   = pTo.ToDateTime(TimeOnly.MinValue);

        // MA des Lohnlaufs: Vertrag der Filiale überlappt die Periode (auch
        // Austritte innerhalb des Monats — «wer war in der Periode aktiv»,
        // Walter 03.08.2026), Phantom-MA (IsPayrollExcluded) raus.
        var employees = await _db.Employments
            .Where(e => e.CompanyProfileId == companyProfileId
                     && e.ContractStartDate <= pToDt
                     && (e.ContractEndDate == null || e.ContractEndDate >= pFromDt))
            .Select(e => e.Employee!)
            .Where(e => !e.IsPayrollExcluded)
            .Distinct()
            .ToListAsync();
        var sorted = employees
            .OrderBy(e => e.FirstName ?? "", StringComparer.Create(new System.Globalization.CultureInfo("de-CH"), true))
            .ThenBy(e => e.LastName ?? "", StringComparer.Create(new System.Globalization.CultureInfo("de-CH"), true))
            .ToList();
        if (sorted.Count == 0)
            return NotFound(new { error = "Keine Mitarbeiter mit Vertrag in dieser Periode." });

        var periode = await _db.PayrollPerioden
            .FirstOrDefaultAsync(p => p.CompanyProfileId == companyProfileId
                                   && p.Year == year && p.Month == month);

        var pdfs = new List<byte[]>();
        foreach (var emp in sorted)
        {
            try
            {
                var slip = await LoadStundenkontrolleSlipAsync(emp.Id, year, month, companyProfileId, periode);
                pdfs.Add(await _stundenkontrollePdf.GenerateAsync(emp.Id, year, month, companyProfileId, slip));
            }
            catch (InvalidOperationException)
            {
                // Einzelner MA ohne generierbares Blatt (z.B. Datenlücke) →
                // überspringen statt das Gesamt-PDF zu verhindern.
            }
        }
        if (pdfs.Count == 0)
            return NotFound(new { error = "Kein Stundenkontrollblatt erzeugbar." });

        using var ms = new MemoryStream();
        using (var writer = new iText.Kernel.Pdf.PdfWriter(ms))
        using (var merged = new iText.Kernel.Pdf.PdfDocument(writer))
        {
            var merger = new iText.Kernel.Utils.PdfMerger(merged);
            foreach (var bytes in pdfs)
            {
                using var src = new iText.Kernel.Pdf.PdfDocument(
                    new iText.Kernel.Pdf.PdfReader(new MemoryStream(bytes)));
                merger.Merge(src, 1, src.GetNumberOfPages());
            }
        }
        return File(ms.ToArray(), "application/pdf",
            $"Stundenkontrolle_Alle_{year}-{month:D2}.pdf");
    }

    [HttpGet("saldo")]
    public async Task<IActionResult> GetSaldo(
        [FromQuery] int employeeId,
        [FromQuery] int year,
        [FromQuery] int month,
        [FromQuery] int companyProfileId)
    {
        if (!await CanAccessBranchAsync(companyProfileId))
            return StatusCode(403, new { error = "Kein Zugriff auf diese Filiale." });
        var saldo = await _db.PayrollSaldos
            .FirstOrDefaultAsync(s => s.EmployeeId       == employeeId
                                   && s.PeriodYear       == year
                                   && s.PeriodMonth      == month
                                   && s.CompanyProfileId == companyProfileId);
        return Ok(saldo);
    }

    // POST /api/payroll/confirm – Lohn bestätigen: Saldo + Snapshot speichern
    [HttpPost("confirm")]
    public async Task<IActionResult> ConfirmPayroll([FromBody] ConfirmPayrollDto dto)
    {
        if (!await CanAccessBranchAsync(dto.CompanyProfileId))
            return StatusCode(403, new { error = "Kein Zugriff auf diese Filiale." });
        // 0) Sequenz-Pflicht (Walter-Vorgabe 16.05.2026, präzisiert 03.08.2026):
        // Solange Definitiv noch «offen» ist und Akonto in einem Zwischenstatus
        // hängt, blockieren — sonst wäre Netto − Akonto instabil.
        // Sobald Definitiv schon läuft/fertig ist, gilt Akonto als erledigt
        // (kein Lock mehr; Mid-flight wird auf UEBERSPRUNGEN geheilt).
        var akontoPeriode = await _db.PayrollPerioden
            .FirstOrDefaultAsync(p => p.CompanyProfileId == dto.CompanyProfileId
                                   && p.Year  == dto.Year
                                   && p.Month == dto.Month);
        // Korrekturlohn (IsCorrection) ist davon ausgenommen — kein Akonto-Bezug.
        if (!dto.IsCorrection && akontoPeriode != null)
        {
            await AkontoDefinitivGuard.TryAbandonMidFlightAsync(_db, akontoPeriode, GetUserIdOrNull());
            if (!AkontoDefinitivGuard.IsAkontoStrangFertig(akontoPeriode.AkontoStatus, akontoPeriode.Status))
            {
                return Conflict(new {
                    error = $"Definitivlohn kann erst bestätigt werden, wenn der Akonto-Lauf "
                          + $"für {dto.Month:00}/{dto.Year} AUSBEZAHLT ist "
                          + $"(aktueller Akonto-Status: {akontoPeriode.AkontoStatus})."
                });
            }
        }

        // 1) Snapshot-Schutz: abgeschlossene Periode → kein Update
        var snapshot = await _db.PayrollSnapshots
            .Include(s => s.Periode)
            .FirstOrDefaultAsync(s => s.EmployeeId == dto.EmployeeId
                                   && s.PayrollPeriodeId == dto.PayrollPeriodeId);

        if (snapshot?.IsFinal == true)
            return Conflict(new { error = "Lohnperiode ist abgeschlossen. Keine Änderungen mehr möglich." });

        // ── Server-autoritativ nachrechnen (Walter-Vorgabe 20.05.2026, Security) ──
        // NIE Beträge aus dem Request-Body übernehmen — ein manipulierter POST
        // dürfte sonst falsche Löhne speichern. Wir rechnen den Lohnzettel mit
        // DERSELBEN Logik wie GET /calculate neu und speichern AUSSCHLIESSLICH die
        // Server-Werte. Die einzige GF-Entscheidung — Ferien-Kürzung anwenden
        // (Art. 329b OR) — kommt als Flag dto.ApplyFerienKuerzung und wird hier
        // mit dem server-berechneten Vorschlag reproduziert. Geldbeträge
        // (Brutto/Netto/SV/QST) sind von dieser Entscheidung NICHT betroffen.
        var calcAction = await _calcEngine.CalculateAsync(
            dto.EmployeeId, dto.Year, dto.Month, dto.CompanyProfileId, dto.IsCorrection);
        if (calcAction is not OkObjectResult okCalc || okCalc.Value is null)
            return calcAction;   // Berechnungs-Fehler (NotFound/BadRequest) 1:1 durchreichen

        var _camelOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var srvNode = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(okCalc.Value, _camelOpts))!;

        if (srvNode["pausiert"] is { } pausNode && pausNode.GetValueKind() == JsonValueKind.True)
            return Conflict(new { error = "Mitarbeiter ist in diesem Monat über die KTG-Versicherung abgerechnet (Pause) — keine Bestätigung möglich." });

        // ── Mindestlohn-Sperre (Walter-Vorgabe 20.05.2026) ─────────────────────
        // Liegt der vertragliche Lohn am Periodenende unter dem L-GAV-Mindestlohn,
        // ist die Bestätigung HART gesperrt — erst Lohn korrigieren. Der Lohn wird
        // aus dem in der Periode gültigen Vertrag genommen (server-autoritativ,
        // nicht aus dem Request). Greift auch wenn ein NEUER Mindestlohn rückwirkend
        // ab einem Datum gilt, das in diese Periode fällt.
        // Korrekturlohn: kein laufender Vertrag → Sperren überspringen.
        var (mwFrom, mwTo) = CalcPeriod(dto.Year, dto.Month);
        var mwFromDt = mwFrom.ToDateTime(TimeOnly.MinValue);
        var mwToDt   = mwTo.ToDateTime(TimeOnly.MinValue);
        // DateTime-Vergleich (NICHT DateOnly.FromDateTime — in EF/Npgsql nicht
        // SQL-übersetzbar → 500). ContractStartDate ist DateTime (date-mid).
        var mwEmp = dto.IsCorrection ? null : await _db.Employments
            .Include(e => e.JobGroup)   // FK-Code für Mindestlohn-Lookup (Walter 26.05.2026)
            .Where(e => e.EmployeeId == dto.EmployeeId
                     && e.IsActive
                     && e.CompanyProfileId == dto.CompanyProfileId
                     && e.ContractStartDate <= mwToDt
                     && (e.ContractEndDate == null || e.ContractEndDate >= mwFromDt))
            .OrderByDescending(e => e.ContractStartDate)
            .FirstOrDefaultAsync();
        if (mwEmp != null)
        {
            // ── Lohnsumme-fehlt-Sperre (Walter-Vorgabe 21.05.2026) ──────────────
            // Gültiger Vertrag aber KEINE Lohnsumme (FIX/FIX-M ohne Monatslohn,
            // UTP/MTP ohne Stundenlohn) → der MA bekäme 0 Lohn (nur Abzüge,
            // negativer Netto). Hart gesperrt, bis ein Lohn erfasst ist.
            // Rule-unabhängig (greift auch wenn keine Mindestlohnregel existiert).
            if (MinimumWageCheckService.IsLohnsummeMissing(
                    mwEmp.EmploymentModel, mwEmp.MonthlySalary, mwEmp.MonthlySalaryFte, mwEmp.HourlyRate))
                return Conflict(new { error = "LOHNSUMME_FEHLT",
                    message = "Vertrag ohne Lohnsumme — bitte zuerst einen Lohn erfassen, bevor der Lohnlauf bestätigt wird." });

            var mwDob = await _db.Employees.Where(e => e.Id == dto.EmployeeId)
                .Select(e => e.DateOfBirth).FirstOrDefaultAsync();
            var mwChk = await _minWage.CheckAsync(
                mwEmp.JobGroup?.Code, mwEmp.EducationLevelCode, mwEmp.EmploymentModel,
                mwEmp.EmploymentPercentage, mwEmp.HourlyRate, mwEmp.MonthlySalary,
                mwDob, mwTo, mwEmp.CompanyProfileId);
            if (mwChk.Status == "UNDERPAID")
                return Conflict(new { error = "MINDESTLOHN_UNTERSCHRITTEN", message = mwChk.Message });
        }

        // ── QST-Pflicht-Check (Walter-Vorgabe 26.05.2026) ──────────────────
        // Wenn der MA QST-pflichtig ist (kein CH, kein C, keine Behörden-
        // Befreiung, kein CH/C-Ehepartner) UND es keine gültige QST-Erfassung
        // am Periodenende gibt → Lohnlauf blocken. Walter's Schweizer Praxis:
        // lieber höchsten Tarif erfassen als gar nichts.
        // Korrekturlohn: keine QST-Pflicht-Sperre (manuelle Positionen).
        if (!dto.IsCorrection)
        {
            var qstChk = await _qstCheck.CheckAsync(dto.EmployeeId, mwTo);
            if (qstChk.IsPflichtOffen)
                return Conflict(new { error = "QST_PFLICHT_OFFEN", message = qstChk.Message });
        }

        decimal SrvDec(string key)
        {
            var n = srvNode[key];
            return n != null && n.GetValueKind() == JsonValueKind.Number ? n.GetValue<decimal>() : 0m;
        }
        decimal srvGross    = SrvDec("totalLohn");
        decimal srvNet      = SrvDec("nettolohn");
        decimal srvAhv      = SrvDec("svBasisAhv");
        decimal srvBvg      = SrvDec("svBasisBvg");
        decimal srvQst      = SrvDec("qstBetrag");
        decimal srvHour     = SrvDec("neuerHourSaldo");
        decimal srvNacht    = SrvDec("neuerNachtSaldo");
        decimal srvNight    = SrvDec("nightHours");
        decimal srvFerGeld  = SrvDec("ferienGeldSaldoNeu");
        decimal srvFerTageBase = SrvDec("ferienTageSaldoNeu");
        decimal srvFeiertag = SrvDec("feiertagTageSaldoNeu");
        decimal srv13Month  = SrvDec("thirteenthMonthly");
        decimal srv13Acc    = SrvDec("thirteenthAccumulated");

        // Ferien-Kürzungs-Vorschlag (nested: ferienKuerzung.vorschlagTage)
        decimal srvVorschlagTage = 0m;
        var kuerzNode = srvNode["ferienKuerzung"];
        if (kuerzNode != null && kuerzNode["vorschlagTage"] is { } vtNode && vtNode.GetValueKind() == JsonValueKind.Number)
            srvVorschlagTage = vtNode.GetValue<decimal>();
        decimal srvFerTage = dto.ApplyFerienKuerzung
            ? Math.Round(srvFerTageBase - srvVorschlagTage, 4)
            : srvFerTageBase;

        // Slip-JSON server-autoritativ + GF-Entscheidung einpatchen, damit der
        // gespeicherte Lohnzettel (PDF/Lohnausweis) zum gespeicherten Saldo passt.
        srvNode["ferienTageSaldoNeu"]           = srvFerTage;
        srvNode["ferienKuerzungAngewendet"]     = dto.ApplyFerienKuerzung;
        srvNode["ferienKuerzungAngewendetTage"] = dto.ApplyFerienKuerzung ? srvVorschlagTage : 0m;
        string srvSlipJson = srvNode.ToJsonString();

        // 2) Saldo speichern (identisch wie /save)
        var saldo = await _db.PayrollSaldos
            .FirstOrDefaultAsync(s => s.EmployeeId    == dto.EmployeeId
                                   && s.PeriodYear    == dto.Year
                                   && s.PeriodMonth   == dto.Month
                                   && s.CompanyProfileId == dto.CompanyProfileId);
        // Walter-Vorgabe 30.06.2026: DateTime.Now, NIE UtcNow — Spalten sind
        // timestamp without time zone. Korrekturlohn legt oft erstmals Snapshot/
        // Saldo an → UtcNow war hier der «ewige Wurm» (Npgsql 500).
        var nowTs = DateTime.Now;
        if (saldo is null)
        {
            saldo = new PayrollSaldo
            {
                EmployeeId       = dto.EmployeeId,
                CompanyProfileId = dto.CompanyProfileId,
                PeriodYear       = dto.Year,
                PeriodMonth      = dto.Month,
                CreatedAt        = nowTs
            };
            _db.PayrollSaldos.Add(saldo);
        }
        // Server-Werte (NICHT dto.*) — siehe autoritative Nachrechnung oben.
        saldo.HourSaldo                  = srvHour;
        saldo.NachtSaldo                 = srvNacht;
        saldo.NightHoursWorked           = srvNight;
        saldo.FerienGeldSaldo            = srvFerGeld;
        saldo.FerienTageSaldo            = srvFerTage;
        saldo.FeiertagTageSaldo          = srvFeiertag;
        saldo.ThirteenthMonthMonthly     = srv13Month;
        saldo.ThirteenthMonthAccumulated = srv13Acc;
        saldo.GrossAmount                = srvGross;
        saldo.NetAmount                  = srvNet;
        saldo.Status                     = "confirmed";
        saldo.UpdatedAt                  = nowTs;

        // 3) Snapshot speichern / aktualisieren
        if (snapshot is null)
        {
            snapshot = new PayrollSnapshot
            {
                PayrollPeriodeId = dto.PayrollPeriodeId,
                EmployeeId       = dto.EmployeeId,
                CompanyProfileId = dto.CompanyProfileId,
                CreatedAt        = nowTs
            };
            _db.PayrollSnapshots.Add(snapshot);
        }
        // Server-Werte + server-autoritatives Slip-JSON (NICHT dto.*).
        snapshot.SlipJson               = srvSlipJson;
        snapshot.Brutto                 = srvGross;
        snapshot.Netto                  = srvNet;
        snapshot.SvBasisAhv             = srvAhv;
        snapshot.SvBasisBvg             = srvBvg;
        snapshot.QstBetrag              = srvQst;
        snapshot.ThirteenthAccumulated  = srv13Acc;
        snapshot.FerienGeldSaldo        = srvFerGeld;
        snapshot.UpdatedAt              = nowTs;

        // 4-Augen-Workflow Walter-Vorgabe 19.05.2026 — Confirm = GF-Freigabe
        // pro MA. HR_BESTAETIGT bleibt unverändert wenn schon weitergerollt
        // (re-confirm während HR-Phase würde sonst HR-Bestätigung verlieren).
        // Hier nur „neu" oder von BERECHNET kommend → FREIGEGEBEN_GF.
        // Nachzügler in bereits provisorischer Periode (Walter Aug 2026,
        // erweitert 03.08.2026): gilt für Korrekturlöhne UND reguläre MA,
        // die erst nachträglich in die Liste kamen (z.B. Austritt in der
        // Periode → is_active=false → fehlte im Lauf). Ein Klick von HR
        // setzt direkt HR_BESTAETIGT — sonst bliebe der Nachzügler nach
        // Confirm noch auf FREIGEGEBEN_GF und bräuchte einen zweiten Klick.
        if (snapshot.Status == "BERECHNET" || string.IsNullOrEmpty(snapshot.Status))
        {
            var actorId = GetUserIdOrNull();
            // akontoPeriode = PayrollPeriode dieser Filiale/Periode (oben geladen)
            if (akontoPeriode != null
                && string.Equals(akontoPeriode.Status, "provisorisch_abgeschlossen", StringComparison.OrdinalIgnoreCase)
                && (User.IsInRole("admin") || User.IsInRole("superuser") || User.IsInRole("buchhaltung")))
            {
                snapshot.Status = "HR_BESTAETIGT";
                snapshot.GfFreigegebenAt ??= nowTs;
                snapshot.GfFreigegebenBy ??= actorId;
                snapshot.HrBestaetigtAt = nowTs;
                snapshot.HrBestaetigtBy = actorId;
            }
            else
            {
                snapshot.Status = "FREIGEGEBEN_GF";
                snapshot.GfFreigegebenAt = nowTs;
                snapshot.GfFreigegebenBy = actorId;
            }
        }

        // Akonto-Bereits-Ausbezahlt-Feld pflegen (Walter-Vorgabe 17.05.2026):
        // wenn ein Akonto-Lauf dieser Periode AUSBEZAHLT ist, persistieren wir
        // den ausbezahlten Netto-Betrag im Snapshot — sowohl für die Audit-
        // Spur als auch damit Jahresauswertungen (Lohnausweis, BFS-LSE) den
        // Akonto-Anteil sauber separieren können.
        var akZ = await _db.AkontoZahlungen
            .Where(z => z.EmployeeId == dto.EmployeeId
                     && z.CompanyProfileId == dto.CompanyProfileId
                     && z.PeriodYear == dto.Year && z.PeriodMonth == dto.Month
                     && z.Status == "AUSBEZAHLT")
            .Select(z => (decimal?)z.NettoAkonto)
            .FirstOrDefaultAsync();
        snapshot.AkontoBereitsAusbezahlt = akZ ?? 0m;

        // KTG-Tagessatz wird on-demand im GET /api/payroll/ktg-tagessatz berechnet
        // (kein Cache mehr \u2014 ersetzt die fr\u00fchere 6-Monats-\u00d8-Logik)

        // ── Lohnabtretungen: Historien-Einträge + bereits_abgezogen pflegen ──
        // Re-Confirm-sicher (idempotent):
        //   1) Alte Entries für diesen Snapshot werden rückgebucht
        //      (BereitsAbgezogen -= alter Betrag) und gelöscht.
        //   2) Neue Entries werden mit Snapshot der Abtretungs-Regel und
        //      der Behörde (Name, IBAN, QR-IBAN) angelegt — bleiben damit
        //      auch nach späteren Behörden-Umbenennungen korrekt.
        //   3) BereitsAbgezogen wird neu hochgezählt.
        // Diese Einträge sind die Grundlage für DTA-Zahlungsexport und
        // Abacus-FIBU-Buchungen.
        List<PayrollLohnAbtretungEntry> existingEntries = new();
        if (snapshot.Id != 0)
        {
            existingEntries = await _db.PayrollLohnAbtretungEntries
                .Where(e => e.PayrollSnapshotId == snapshot.Id)
                .ToListAsync();
        }
        foreach (var old in existingEntries)
        {
            var laOld = await _db.EmployeeLohnAssignments.FindAsync(old.EmployeeLohnAssignmentId);
            if (laOld != null)
            {
                laOld.BereitsAbgezogen = Math.Max(0, Math.Round(laOld.BereitsAbgezogen - old.Betrag, 2));
                laOld.UpdatedAt        = nowTs;
            }
            _db.PayrollLohnAbtretungEntries.Remove(old);
        }

        if (dto.LohnAbtretungen is { Length: > 0 })
        {
            foreach (var ab in dto.LohnAbtretungen)
            {
                var la = await _db.EmployeeLohnAssignments
                    .Include(x => x.Behoerde)
                    .FirstOrDefaultAsync(x => x.Id == ab.AssignmentId);
                if (la == null) continue;

                decimal betrag = Math.Round(ab.Betrag, 2);
                if (betrag <= 0) continue;

                decimal vorher = la.BereitsAbgezogen;
                la.BereitsAbgezogen = Math.Round(vorher + betrag, 2);
                la.UpdatedAt        = nowTs;

                _db.PayrollLohnAbtretungEntries.Add(new PayrollLohnAbtretungEntry
                {
                    Snapshot                 = snapshot,         // EF setzt FK nach SaveChanges
                    EmployeeLohnAssignmentId = la.Id,
                    EmployeeId               = la.EmployeeId,
                    BehoerdeId               = la.BehoerdeId,
                    PeriodYear               = dto.Year,
                    PeriodMonth              = dto.Month,
                    Bezeichnung              = la.Bezeichnung,
                    ReferenzAmt              = la.ReferenzAmt,
                    ZahlungsReferenz         = la.ZahlungsReferenz,
                    BehoerdeName             = la.Behoerde?.Name,
                    Iban                     = la.Behoerde?.Iban,
                    QrIban                   = la.Behoerde?.QrIban,
                    Betrag                   = betrag,
                    BereitsAbgezogenVorher   = vorher,
                    BereitsAbgezogenNachher  = la.BereitsAbgezogen,
                    CreatedAt                = nowTs
                });
            }
        }

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Klartext statt nacktem 500 — typisch war timestamptz + DateTime.Now
            // auf employee_lohn_assignment.updated_at (Lohnabtretung).
            var detail = ex.InnerException?.Message ?? ex.Message;
            return StatusCode(500, new {
                error = "CONFIRM_FAILED",
                message = $"Lohn-Bestätigung fehlgeschlagen: {detail}"
            });
        }

        // Uniformen-Depot: nach Confirm Status setzen (Refund / Verfall).
        await _uniformDepot.ApplyAfterConfirmAsync(dto.EmployeeId, dto.Year, dto.Month);

        return Ok(new { snapshotId = snapshot.Id, message = "Lohn bestätigt und gespeichert." });
    }

    // POST /api/payroll/reopen – Bestätigten Lohn wieder eröffnen
    // Setzt den Saldo zurück auf "draft", entfernt die Lohnabtretungs-
    // Historien-Einträge für diesen Snapshot und macht die
    // bereits_abgezogen-Hochzählung rückgängig. Nur möglich solange die
    // Periode noch nicht final abgeschlossen ist.
    //
    // Robustheit: Der Snapshot wird zuerst über den Periode-Kontext
    // (EmployeeId + CompanyProfileId + Year + Month) gesucht, und erst als
    // Fallback über die rohe PayrollPeriodeId. Damit funktioniert Reopen
    // auch wenn die Periode zwischenzeitlich neu angelegt wurde und das
    // Frontend eine andere PayrollPeriodeId mitschickt als am Snapshot hängt.
    //
    // Recovery: Falls kein Snapshot existiert, aber der Saldo auf "confirmed"
    // steht (Inkonsistenz z.B. aus alten Datenbeständen), wird der Saldo
    // trotzdem auf "draft" zurückgesetzt, damit der User nicht feststeckt.
    [HttpPost("reopen")]
    public async Task<IActionResult> ReopenPayroll([FromBody] ReopenPayrollDto dto)
    {
        if (!await CanAccessBranchAsync(dto.CompanyProfileId))
            return StatusCode(403, new { error = "Kein Zugriff auf diese Filiale." });
        // 1) Snapshot finden — primär über Periode-Kontext (Year+Month+Company),
        //    Fallback über die vom Frontend mitgegebene PayrollPeriodeId.
        var snapshot = await _db.PayrollSnapshots
            .Include(s => s.Periode)
            .Where(s => s.EmployeeId == dto.EmployeeId
                     && s.CompanyProfileId == dto.CompanyProfileId
                     && s.Periode != null
                     && s.Periode.Year  == dto.Year
                     && s.Periode.Month == dto.Month)
            .OrderByDescending(s => s.UpdatedAt)
            .FirstOrDefaultAsync();

        if (snapshot is null)
        {
            snapshot = await _db.PayrollSnapshots
                .Include(s => s.Periode)
                .FirstOrDefaultAsync(s => s.EmployeeId == dto.EmployeeId
                                       && s.PayrollPeriodeId == dto.PayrollPeriodeId);
        }

        // 2) Saldo laden (für Status-Check und Recovery-Pfad)
        var saldo = await _db.PayrollSaldos
            .FirstOrDefaultAsync(s => s.EmployeeId       == dto.EmployeeId
                                   && s.PeriodYear       == dto.Year
                                   && s.PeriodMonth      == dto.Month
                                   && s.CompanyProfileId == dto.CompanyProfileId);

        // 3) Schutz: definitiv abgeschlossene Periode kann nicht wieder eröffnet werden.
        // Bei "provisorisch_abgeschlossen" muss zuerst die Periode via
        // /api/payroll-perioden/{id}/zurueck-an-gf zurückgegeben werden, dann
        // ist Reopen einzelner Saldi wieder möglich (durch HR/Admin).
        if (snapshot?.Periode != null && snapshot.Periode.Status == "abgeschlossen")
            return Conflict(new { error = "Lohnperiode ist definitiv abgeschlossen. Wieder-Eröffnen nur durch Admin via Lohnlauf-Wiederöffnen." });
        if (snapshot?.Periode != null && snapshot.Periode.Status == "provisorisch_abgeschlossen")
            return Conflict(new { error = "Lohnperiode ist provisorisch abgeschlossen — bitte zuerst über HR-Bereich → Lohnlauf an GF zurückgeben." });
        if (snapshot?.IsFinal == true)
            return Conflict(new { error = "Lohnperiode ist abgeschlossen. Wieder-Eröffnen nicht mehr möglich." });

        // 4) Recovery-Fall: kein Snapshot, aber Saldo existiert → zurücksetzen
        // Wir akzeptieren JEDEN Saldo (nicht nur "confirmed"), weil der
        // Reopen-Button im Frontend nur erscheint, wenn die GET-Saldo-Route
        // den Saldo als bestätigt liefert. Falls es trotzdem zu einem
        // Status-Mismatch kommt (Whitespace, Casing, alte Daten), soll der
        // Operator das Resetten können.
        if (snapshot is null)
        {
            if (saldo is null)
                return NotFound(new {
                    error = $"Kein Saldo gefunden für Mitarbeiter {dto.EmployeeId}, " +
                            $"Periode {dto.Year}/{dto.Month:D2}, Filiale {dto.CompanyProfileId}."
                });

            string altStatus = saldo.Status ?? "(null)";
            saldo.Status    = "draft";
            saldo.UpdatedAt = DateTime.Now; // System-Regel: Lokalzeit (Walter 04.08.2026)
            await _db.SaveChangesAsync();
            return Ok(new {
                message  = $"Saldo zurückgesetzt (vorheriger Status: '{altStatus}'). Kein Snapshot gefunden — vermutlich Altbestand. Bitte Lohn neu prüfen und bestätigen.",
                recovery = true
            });
        }

        // 5) Lohnabtretungs-Entries rückbuchen und löschen
        var existingEntries = await _db.PayrollLohnAbtretungEntries
            .Where(e => e.PayrollSnapshotId == snapshot.Id)
            .ToListAsync();
        foreach (var old in existingEntries)
        {
            var la = await _db.EmployeeLohnAssignments.FindAsync(old.EmployeeLohnAssignmentId);
            if (la != null)
            {
                la.BereitsAbgezogen = Math.Max(0, Math.Round(la.BereitsAbgezogen - old.Betrag, 2));
                la.UpdatedAt        = DateTime.Now;
            }
            _db.PayrollLohnAbtretungEntries.Remove(old);
        }

        // 6) Saldo zurück auf draft setzen
        if (saldo != null)
        {
            saldo.Status    = "draft";
            saldo.UpdatedAt = DateTime.Now; // System-Regel: Lokalzeit (Walter 04.08.2026)
        }

        // 7) Snapshot löschen — sonst zählt der MA in loadLohnList weiter als
        //    "bestätigt" (grüner Haken bleibt). Beim nächsten Confirm wird ein
        //    neuer Snapshot mit den aktuellen Werten erzeugt.
        _db.PayrollSnapshots.Remove(snapshot);

        await _db.SaveChangesAsync();
        return Ok(new { message = "Lohnzettel wieder eröffnet. Absenzen und Zulagen können erneut bearbeitet werden." });
    }

    // ─── HR per-MA-Bestätigung (Walter 19.05.2026, analog Akonto) ──────────
    // POST /api/payroll/hr-bestaetigen/{snapshotId} — admin/superuser only.
    // Setzt PayrollSnapshot.Status = HR_BESTAETIGT für einen einzelnen
    // Lohnzettel. Voraussetzung: Periode ist provisorisch_abgeschlossen,
    // Snapshot-Status ist FREIGEGEBEN_GF.
    [Authorize(Roles = "admin,superuser")]
    [HttpPost("hr-bestaetigen/{snapshotId:int}")]
    public async Task<IActionResult> HrBestaetigen(int snapshotId)
    {
        var snap = await _db.PayrollSnapshots.Include(s => s.Periode)
                            .FirstOrDefaultAsync(s => s.Id == snapshotId);
        if (snap is null) return NotFound();
        if (!await CanAccessBranchAsync(snap.CompanyProfileId))
            return StatusCode(403, new { error = "Kein Zugriff auf diese Filiale." });
        if (snap.IsFinal)  return Conflict(new { error = "Snapshot ist final — kann nicht mehr verändert werden." });
        if (snap.Periode?.Status != "provisorisch_abgeschlossen")
            return Conflict(new { error = "HR-Bestätigung nur in provisorisch abgeschlossener Periode möglich." });
        // Idempotenz (Walter-Bug 17.08.2026, «da stockt es manchmal»): Doppelklick
        // oder veraltete Liste — ist der Snapshot SCHON HR-bestätigt, ist das Ziel
        // erreicht → Ok statt 409, das Frontend synct sich einfach neu.
        if (snap.Status == "HR_BESTAETIGT")
            return Ok(new { snap.Id, snap.Status, snap.HrBestaetigtAt, schonBestaetigt = true });
        if (snap.Status != "FREIGEGEBEN_GF")
            return Conflict(new { error = $"Snapshot-Status ist {snap.Status} (erwartet FREIGEGEBEN_GF)." });

        snap.Status         = "HR_BESTAETIGT";
        snap.HrBestaetigtAt = DateTime.Now; // System-Regel: Lokalzeit (Walter 04.08.2026)
        snap.HrBestaetigtBy = GetUserIdOrNull();
        snap.UpdatedAt      = DateTime.Now;
        await _db.SaveChangesAsync();
        return Ok(new { snap.Id, snap.Status, snap.HrBestaetigtAt });
    }

    // POST /api/payroll/hr-zurueckziehen/{snapshotId} — admin/superuser only.
    // Setzt HR_BESTAETIGT zurück auf FREIGEGEBEN_GF.
    [Authorize(Roles = "admin,superuser")]
    [HttpPost("hr-zurueckziehen/{snapshotId:int}")]
    public async Task<IActionResult> HrZurueckziehen(int snapshotId)
    {
        var snap = await _db.PayrollSnapshots.Include(s => s.Periode)
                            .FirstOrDefaultAsync(s => s.Id == snapshotId);
        if (snap is null) return NotFound();
        if (!await CanAccessBranchAsync(snap.CompanyProfileId))
            return StatusCode(403, new { error = "Kein Zugriff auf diese Filiale." });
        if (snap.IsFinal) return Conflict(new { error = "Snapshot ist final." });
        // Idempotenz (analog HrBestaetigen): schon zurückgezogen → Ok.
        if (snap.Status == "FREIGEGEBEN_GF")
            return Ok(new { snap.Id, snap.Status, schonZurueckgezogen = true });
        if (snap.Status != "HR_BESTAETIGT")
            return Conflict(new { error = $"Snapshot-Status ist {snap.Status} (erwartet HR_BESTAETIGT)." });

        snap.Status         = "FREIGEGEBEN_GF";
        snap.HrBestaetigtAt = null;
        snap.HrBestaetigtBy = null;
        snap.UpdatedAt      = DateTime.Now;
        await _db.SaveChangesAsync();
        return Ok(new { snap.Id, snap.Status });
    }

    // GET /api/payroll/ktg-tagessatz?employeeId=X&companyProfileId=Y
    // Liefert den KTG/UVG-Tagessatz nach Spezialistenvorgabe (Regel A/B).
    [HttpGet("ktg-tagessatz")]
    public async Task<IActionResult> GetKtgTagessatz(
        [FromQuery] int employeeId,
        [FromQuery] int companyProfileId)
    {
        if (!await CanAccessBranchAsync(companyProfileId))
            return StatusCode(403, new { error = "Kein Zugriff auf diese Filiale." });
        var result = await _ktgService.CalculateAsync(employeeId, companyProfileId);
        if (result is null)
            return NotFound(new { error = "Kein aktives Anstellungsverh\u00e4ltnis." });
        return Ok(result);
    }

    // ───────────────────────────────────────────────────────────────────
    // Lohnabtretungs-Historie — pro Lohnlauf × Abtretung ein Eintrag.
    // Filter: employeeId, behoerdeId, year, monthFrom/monthTo, onlyNotExportedFibu/Dta
    // Nutzung: Reporting pro MA, DTA-Zahlungsexport aggregiert pro Behörde,
    //          Abacus-FIBU-Buchungslauf.
    // ───────────────────────────────────────────────────────────────────
    // Walter 09.06.2026: Reporting-Endpunkte filterfrei über alle Filialen —
    // bewusst auf admin/superuser/buchhaltung beschränkt (kein GF-Zugriff).
    [Authorize(Roles = "admin,superuser,buchhaltung")]
    [HttpGet("lohn-abtretungen/history")]
    public async Task<IActionResult> GetLohnAbtretungHistory(
        [FromQuery] int? employeeId,
        [FromQuery] int? behoerdeId,
        [FromQuery] int? year,
        [FromQuery] int? monthFrom,
        [FromQuery] int? monthTo,
        [FromQuery] bool onlyNotExportedFibu = false,
        [FromQuery] bool onlyNotExportedDta  = false)
    {
        var q = _db.PayrollLohnAbtretungEntries.AsQueryable();

        if (employeeId.HasValue) q = q.Where(e => e.EmployeeId == employeeId.Value);
        if (behoerdeId.HasValue) q = q.Where(e => e.BehoerdeId == behoerdeId.Value);
        if (year.HasValue)       q = q.Where(e => e.PeriodYear == year.Value);
        if (monthFrom.HasValue)  q = q.Where(e => e.PeriodMonth >= monthFrom.Value);
        if (monthTo.HasValue)    q = q.Where(e => e.PeriodMonth <= monthTo.Value);
        if (onlyNotExportedFibu) q = q.Where(e => e.FibuExportiertAm == null);
        if (onlyNotExportedDta)  q = q.Where(e => e.DtaExportiertAm  == null);

        var list = await q
            .OrderBy(e => e.PeriodYear).ThenBy(e => e.PeriodMonth)
            .ThenBy(e => e.BehoerdeName).ThenBy(e => e.EmployeeId)
            .Select(e => new {
                id                      = e.Id,
                payrollSnapshotId       = e.PayrollSnapshotId,
                assignmentId            = e.EmployeeLohnAssignmentId,
                employeeId              = e.EmployeeId,
                behoerdeId              = e.BehoerdeId,
                periodYear              = e.PeriodYear,
                periodMonth             = e.PeriodMonth,
                bezeichnung             = e.Bezeichnung,
                behoerdeName            = e.BehoerdeName,
                iban                    = e.Iban,
                qrIban                  = e.QrIban,
                referenzAmt             = e.ReferenzAmt,
                zahlungsReferenz        = e.ZahlungsReferenz,
                betrag                  = e.Betrag,
                bereitsAbgezogenVorher  = e.BereitsAbgezogenVorher,
                bereitsAbgezogenNachher = e.BereitsAbgezogenNachher,
                fibuBelegnr             = e.FibuBelegnr,
                fibuExportiertAm        = e.FibuExportiertAm,
                dtaExportiertAm         = e.DtaExportiertAm,
                dtaExportRef            = e.DtaExportRef,
                createdAt               = e.CreatedAt
            })
            .ToListAsync();

        var total = list.Sum(x => x.betrag);
        return Ok(new { total, count = list.Count, entries = list });
    }

    // GET /api/payroll/lohn-abtretungen/summary?year=2025&behoerdeId=X
    // Aggregiert pro Behörde × Monat — ideal für FIBU-/DTA-Übersicht.
    [Authorize(Roles = "admin,superuser,buchhaltung")]
    [HttpGet("lohn-abtretungen/summary")]
    public async Task<IActionResult> GetLohnAbtretungSummary(
        [FromQuery] int? year,
        [FromQuery] int? behoerdeId)
    {
        var q = _db.PayrollLohnAbtretungEntries.AsQueryable();
        if (year.HasValue)       q = q.Where(e => e.PeriodYear == year.Value);
        if (behoerdeId.HasValue) q = q.Where(e => e.BehoerdeId == behoerdeId.Value);

        var grouped = await q
            .GroupBy(e => new { e.BehoerdeId, e.BehoerdeName, e.PeriodYear, e.PeriodMonth })
            .Select(g => new {
                behoerdeId   = g.Key.BehoerdeId,
                behoerdeName = g.Key.BehoerdeName,
                periodYear   = g.Key.PeriodYear,
                periodMonth  = g.Key.PeriodMonth,
                anzahl       = g.Count(),
                total        = g.Sum(e => e.Betrag)
            })
            .OrderBy(x => x.periodYear).ThenBy(x => x.periodMonth).ThenBy(x => x.behoerdeName)
            .ToListAsync();

        return Ok(grouped);
    }

    // GET /api/payroll/snapshot?periodeId=X&employeeId=Y
    [HttpGet("snapshot")]
    public async Task<IActionResult> GetSnapshot([FromQuery] int periodeId, [FromQuery] int employeeId)
    {
        var snap = await _db.PayrollSnapshots
            .FirstOrDefaultAsync(s => s.PayrollPeriodeId == periodeId && s.EmployeeId == employeeId);
        if (snap is null) return NotFound();
        if (!await CanAccessBranchAsync(snap.CompanyProfileId))
            return StatusCode(403, new { error = "Kein Zugriff auf diese Filiale." });
        return Ok(new { snap.Id, snap.IsFinal, snap.SlipJson, snap.Brutto, snap.Netto,
                        snap.CreatedAt, snap.UpdatedAt });
    }

    // GET /api/payroll/snapshot/{id}/print – Snapshot für Nachdruck
    [HttpGet("snapshot/{id}/print")]
    public async Task<IActionResult> PrintSnapshot(int id)
    {
        var snap = await _db.PayrollSnapshots
            .Include(s => s.Periode)
            .FirstOrDefaultAsync(s => s.Id == id);
        if (snap is null) return NotFound();
        if (!await CanAccessBranchAsync(snap.CompanyProfileId))
            return StatusCode(403, new { error = "Kein Zugriff auf diese Filiale." });
        // SlipJson direkt zurückgeben (wird im Frontend gleich gerendert wie live-Berechnung)
        return Content(snap.SlipJson, "application/json");
    }




}
