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
public class PayrollController : ControllerBase
{
    private int? GetUserIdOrNull() =>
        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var v) ? v : (int?)null;

    private readonly AppDbContext _db;
    private readonly QuellensteuerTarifService _tarifService;
    private readonly KtgTagessatzService _ktgService;
    private readonly KarenzService _karenz;
    private readonly LgavBeitragService _lgav;
    private readonly FerienKuerzungService _ferienKuerzung;
    private readonly PayrollPdfService _payrollPdf;
    private readonly PayrollCalculationEngine _calcEngine;
    private readonly MinimumWageCheckService _minWage;
    private readonly LohnSaldoListePdfService _saldoListePdf;
    private readonly FibuJournalService _fibuJournal;
    private readonly SnapshotRecomputeService _snapshotRecompute;

    public PayrollController(
        AppDbContext db,
        QuellensteuerTarifService tarifService,
        KtgTagessatzService ktgService,
        KarenzService karenz,
        LgavBeitragService lgav,
        FerienKuerzungService ferienKuerzung,
        PayrollPdfService payrollPdf,
        PayrollCalculationEngine calcEngine,
        MinimumWageCheckService minWage,
        LohnSaldoListePdfService saldoListePdf,
        FibuJournalService fibuJournal,
        SnapshotRecomputeService snapshotRecompute)
    {
        _db             = db;
        _tarifService   = tarifService;
        _ktgService     = ktgService;
        _karenz         = karenz;
        _lgav           = lgav;
        _ferienKuerzung = ferienKuerzung;
        _payrollPdf     = payrollPdf;
        _calcEngine     = calcEngine;
        _minWage        = minWage;
        _saldoListePdf  = saldoListePdf;
        _fibuJournal    = fibuJournal;
        _snapshotRecompute = snapshotRecompute;
    }

    // GET /api/payroll/fibu-journal?companyProfileId&year&month  → Fibu-Journal-PDF
    // (Buchungsjournal aus den bestätigten Snapshots, Walter 22.05.2026). HR-only.
    [HttpGet("fibu-journal")]
    public async Task<IActionResult> FibuJournal(
        [FromQuery] int companyProfileId, [FromQuery] int year, [FromQuery] int month)
    {
        try
        {
            var pdf = await _fibuJournal.GeneratePdfAsync(companyProfileId, year, month);
            return File(pdf, "application/pdf");
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
            s.UpdatedAt = DateTime.UtcNow;
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
        var updated = await _snapshotRecompute.RecomputeAsync(companyProfileId, year, month);
        return Ok(new { updated, total });
    }

    // ── Saldo-Listen zum Definitiv-Abschluss (Walter-Vorgabe 21.05.2026) ──────
    // Zwei PDFs pro Filiale + Periode, on-demand aus den persistierten
    // PayrollSaldo-Zeilen. „buchhaltung" = alle Saldi + Brutto/Netto + IBAN;
    // „gf" = kompakte Übersicht (UTP ohne 13. ML). HR-only Rollen reichen
    // (DefaultPolicy admin/superuser/user); read-only, kein Edit-Lock-Belang.
    [HttpGet("saldo-liste-buchhaltung")]
    public async Task<IActionResult> SaldoListeBuchhaltung(
        [FromQuery] int companyProfileId, [FromQuery] int year, [FromQuery] int month)
    {
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
        try
        {
            var pdf = await _saldoListePdf.GenerateGfAsync(companyProfileId, year, month);
            return File(pdf, "application/pdf");
        }
        catch (InvalidOperationException ex) { return NotFound(new { error = ex.Message }); }
    }

    // GET /api/payroll/calculate?employeeId=X&year=Y&month=M&companyProfileId=Z
    [HttpGet("calculate")]
    public Task<IActionResult> Calculate(
        [FromQuery] int employeeId,
        [FromQuery] int year,
        [FromQuery] int month,
        [FromQuery] int companyProfileId)
        => _calcEngine.CalculateAsync(employeeId, year, month, companyProfileId);

    // POST /api/payroll/save – Saldo speichern (Zwischenstand, "draft")
    //
    // WICHTIG: Dieser Endpunkt darf den Status NIE auf "confirmed" setzen.
    // Der einzige legitime Weg zu "confirmed" ist POST /api/payroll/confirm,
    // weil nur dort zusammen mit dem Saldo auch der PayrollSnapshot geschrieben
    // wird. Würde /save ein "confirmed" durchlassen, entstünden Saldos ohne
    // zugehörigen Snapshot → das bricht den Reopen-Flow und die Jahresausweise.
    [HttpPost("save")]
    public async Task<IActionResult> SaveSaldo([FromBody] SaveSaldoDto dto)
    {
        var existing = await _db.PayrollSaldos
            .FirstOrDefaultAsync(s => s.EmployeeId    == dto.EmployeeId
                                   && s.PeriodYear    == dto.Year
                                   && s.PeriodMonth   == dto.Month
                                   && s.CompanyProfileId == dto.CompanyProfileId);

        // Einen bereits bestätigten Saldo über /save zu überschreiben würde den
        // Snapshot-Status aus der Synchronität kippen. Dafür gibt es /reopen.
        if (existing is not null && existing.Status == "confirmed")
            return Conflict(new {
                error = "Saldo ist bereits bestätigt. Bitte zuerst über /reopen wieder eröffnen."
            });

        if (existing is null)
        {
            existing = new PayrollSaldo
            {
                EmployeeId       = dto.EmployeeId,
                CompanyProfileId = dto.CompanyProfileId,
                PeriodYear       = dto.Year,
                PeriodMonth      = dto.Month,
                CreatedAt        = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
            };
            _db.PayrollSaldos.Add(existing);
        }

        existing.HourSaldo                   = dto.HourSaldo;
        existing.NachtSaldo                  = dto.NachtSaldo;
        existing.NightHoursWorked            = dto.NightHoursWorked;
        existing.FerienGeldSaldo             = dto.FerienGeldSaldo;
        existing.FerienTageSaldo             = dto.FerienTageSaldo;
        existing.FeiertagTageSaldo           = dto.FeiertagTageSaldo;
        existing.ThirteenthMonthMonthly      = dto.ThirteenthMonthMonthly;
        existing.ThirteenthMonthAccumulated  = dto.ThirteenthMonthAccumulated;
        existing.GrossAmount                 = dto.GrossAmount;
        existing.NetAmount                   = dto.NetAmount;
        // Nie "confirmed" über /save — das ist /confirm vorbehalten.
        existing.Status                      = (dto.Status == "confirmed") ? "draft" : dto.Status;
        existing.UpdatedAt                   = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

        await _db.SaveChangesAsync();
        return Ok(existing);
    }

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

    [HttpGet("saldo")]
    public async Task<IActionResult> GetSaldo(
        [FromQuery] int employeeId,
        [FromQuery] int year,
        [FromQuery] int month,
        [FromQuery] int companyProfileId)
    {
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
        // 0) Sequenz-Pflicht (Walter-Vorgabe 16.05.2026): sobald der Akonto-Lauf
        // für diese Periode begonnen wurde, muss er erst AUSBEZAHLT sein, bevor
        // der Definitivlohn bestätigt werden darf. Sonst wäre die Restzahlungs-
        // Berechnung (Netto − Akonto) instabil — der Akonto-Betrag könnte sich
        // ja noch ändern. Backend-Guard ist hier die zweite Verteidigungslinie;
        // das Frontend versteckt den Bestätigen-Button bereits (#lohnDefinitivLockBanner).
        //
        // OFFEN (= Akonto nie gestartet) bleibt erlaubt — Walter kann den
        // Akonto-Workflow bewusst überspringen und direkt definitiv abrechnen
        // (z.B. für Vor-Akonto-Perioden oder Filialen ohne Akonto-Termin).
        var akontoPeriode = await _db.PayrollPerioden
            .FirstOrDefaultAsync(p => p.CompanyProfileId == dto.CompanyProfileId
                                   && p.Year  == dto.Year
                                   && p.Month == dto.Month);
        if (akontoPeriode != null
            && akontoPeriode.AkontoStatus != "AUSBEZAHLT"
            && akontoPeriode.AkontoStatus != "OFFEN")
        {
            return Conflict(new {
                error = $"Definitivlohn kann erst bestätigt werden, wenn der Akonto-Lauf "
                      + $"für {dto.Month:00}/{dto.Year} AUSBEZAHLT ist "
                      + $"(aktueller Akonto-Status: {akontoPeriode.AkontoStatus})."
            });
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
        var calcAction = await _calcEngine.CalculateAsync(dto.EmployeeId, dto.Year, dto.Month, dto.CompanyProfileId);
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
        var (mwFrom, mwTo) = CalcPeriod(dto.Year, dto.Month);
        var mwFromDt = mwFrom.ToDateTime(TimeOnly.MinValue);
        var mwToDt   = mwTo.ToDateTime(TimeOnly.MinValue);
        // DateTime-Vergleich (NICHT DateOnly.FromDateTime — in EF/Npgsql nicht
        // SQL-übersetzbar → 500). ContractStartDate ist DateTime (date-mid).
        var mwEmp = await _db.Employments
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
                mwEmp.JobTitle, mwEmp.EducationLevelCode, mwEmp.EmploymentModel,
                mwEmp.EmploymentPercentage, mwEmp.HourlyRate, mwEmp.MonthlySalary,
                mwDob, mwTo, mwEmp.CompanyProfileId);
            if (mwChk.Status == "UNDERPAID")
                return Conflict(new { error = "MINDESTLOHN_UNTERSCHRITTEN", message = mwChk.Message });
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
        if (saldo is null)
        {
            saldo = new PayrollSaldo
            {
                EmployeeId       = dto.EmployeeId,
                CompanyProfileId = dto.CompanyProfileId,
                PeriodYear       = dto.Year,
                PeriodMonth      = dto.Month,
                CreatedAt        = DateTime.UtcNow
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
        saldo.UpdatedAt                  = DateTime.UtcNow;

        // 3) Snapshot speichern / aktualisieren
        if (snapshot is null)
        {
            snapshot = new PayrollSnapshot
            {
                PayrollPeriodeId = dto.PayrollPeriodeId,
                EmployeeId       = dto.EmployeeId,
                CompanyProfileId = dto.CompanyProfileId,
                CreatedAt        = DateTime.UtcNow
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
        snapshot.UpdatedAt              = DateTime.UtcNow;

        // 4-Augen-Workflow Walter-Vorgabe 19.05.2026 — Confirm = GF-Freigabe
        // pro MA. HR_BESTAETIGT bleibt unverändert wenn schon weitergerollt
        // (re-confirm während HR-Phase würde sonst HR-Bestätigung verlieren).
        // Hier nur „neu" oder von BERECHNET kommend → FREIGEGEBEN_GF.
        if (snapshot.Status == "BERECHNET" || string.IsNullOrEmpty(snapshot.Status))
        {
            snapshot.Status = "FREIGEGEBEN_GF";
            snapshot.GfFreigegebenAt = DateTime.UtcNow;
            snapshot.GfFreigegebenBy = GetUserIdOrNull();
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
                laOld.UpdatedAt        = DateTime.UtcNow;
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
                la.UpdatedAt        = DateTime.UtcNow;

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
                    CreatedAt                = DateTime.UtcNow
                });
            }
        }

        await _db.SaveChangesAsync();
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
            saldo.UpdatedAt = DateTime.UtcNow;
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
                la.UpdatedAt        = DateTime.UtcNow;
            }
            _db.PayrollLohnAbtretungEntries.Remove(old);
        }

        // 6) Saldo zurück auf draft setzen
        if (saldo != null)
        {
            saldo.Status    = "draft";
            saldo.UpdatedAt = DateTime.UtcNow;
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
        if (snap.IsFinal)  return Conflict(new { error = "Snapshot ist final — kann nicht mehr verändert werden." });
        if (snap.Periode?.Status != "provisorisch_abgeschlossen")
            return Conflict(new { error = "HR-Bestätigung nur in provisorisch abgeschlossener Periode möglich." });
        if (snap.Status != "FREIGEGEBEN_GF")
            return Conflict(new { error = $"Snapshot-Status ist {snap.Status} (erwartet FREIGEGEBEN_GF)." });

        snap.Status         = "HR_BESTAETIGT";
        snap.HrBestaetigtAt = DateTime.UtcNow;
        snap.HrBestaetigtBy = GetUserIdOrNull();
        snap.UpdatedAt      = DateTime.UtcNow;
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
        if (snap.IsFinal) return Conflict(new { error = "Snapshot ist final." });
        if (snap.Status != "HR_BESTAETIGT")
            return Conflict(new { error = $"Snapshot-Status ist {snap.Status} (erwartet HR_BESTAETIGT)." });

        snap.Status         = "FREIGEGEBEN_GF";
        snap.HrBestaetigtAt = null;
        snap.HrBestaetigtBy = null;
        snap.UpdatedAt      = DateTime.UtcNow;
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
        // SlipJson direkt zurückgeben (wird im Frontend gleich gerendert wie live-Berechnung)
        return Content(snap.SlipJson, "application/json");
    }




}
