using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

[ApiController]
[Route("api/payroll-perioden")]
public class PayrollPeriodeController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly HrSystem.Services.LohnlaufService _lohnlaufSvc;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly HrSystem.Services.SnapshotRecomputeService _snapshotRecompute;

    public PayrollPeriodeController(AppDbContext db, HrSystem.Services.LohnlaufService lohnlaufSvc, IServiceScopeFactory scopeFactory, HrSystem.Services.SnapshotRecomputeService snapshotRecompute)
    {
        _lohnlaufSvc = lohnlaufSvc;
        _db = db;
        _scopeFactory = scopeFactory;
        _snapshotRecompute = snapshotRecompute;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  PERIODEN  (konkrete Lohnperioden)
    //
    //  Walter-Vorgabe 20.05.2026: Die Lohnperiode ist IMMER der Kalendermonat
    //  (1.–letzter Tag). Die frühere Periodenregel-Konfiguration
    //  (PayrollPeriodeConfig, Starttag 21/1, Übergangs-Lohnläufe) ist komplett
    //  entfernt — gesetzliche Berechnungen (QST, ALV, AHV) laufen ohnehin
    //  kalendermonatlich, und der Akonto-Lauf deckt die Zahlung vor Monatsende ab.
    // ══════════════════════════════════════════════════════════════════════════

    // GET /api/payroll-perioden?companyProfileId=X&year=Y
    [HttpGet]
    public async Task<IActionResult> GetPerioden(
        [FromQuery] int companyProfileId,
        [FromQuery] int? year)
    {
        var q = _db.PayrollPerioden
            .Where(p => p.CompanyProfileId == companyProfileId);

        if (year.HasValue)
            q = q.Where(p => p.Year == year.Value);

        var list = await q
            .OrderByDescending(p => p.Year)
            .ThenByDescending(p => p.Month)
            .Select(p => new {
                p.Id, p.CompanyProfileId,
                p.Year, p.Month, p.Label,
                PeriodFrom = p.PeriodFrom.ToString("yyyy-MM-dd"),
                PeriodTo   = p.PeriodTo.ToString("yyyy-MM-dd"),
                p.Status,
                p.AbgeschlossenAm, p.AbgeschlossenVon,
                p.CreatedAt,
                SnapshotCount = p.Snapshots.Count,
                FinalCount    = p.Snapshots.Count(s => s.IsFinal),
                // Akonto-Workflow (Walter-Vorgabe 17.05.2026): pro Periode
                // gibt's einen parallelen Akonto-Status + Counter wieviele
                // MA-Lohnblätter schon berechnet sind. Wird im UI zusammen
                // mit dem Definitiv-Status als zweite Pille gezeigt, damit
                // sichtbar ist welche Periode "in Verarbeitung" ist.
                p.AkontoStatus,
                p.AkontoGfStartedAt,
                p.AkontoGfSentAt,
                p.AkontoHrFreigegebenAt,
                p.AkontoAusbezahltAt,
                // Bank-Ausführungsdatum (ReqdExctnDt im DTA) je Strang — für die
                // Anzeige des Zahldatums + den Admin-Reset-Lock im Lohnperioden-Modul
                // (Walter-Vorgabe 20.05.2026). Roh als DateOnly? zurückgeben
                // (System.Text.Json serialisiert als "yyyy-MM-dd"); das Frontend
                // (fmtDateDe) formatiert auf TT.MM.JJJJ.
                p.Auszahlungsdatum,
                p.AkontoAuszahlungsdatum,
                // Abacus-Buchungsnummer (Fibu-Seite: Prefill + Vorschlag +1).
                p.FibuBuchungsnummer,
                AkontoCount = _db.AkontoZahlungen
                    .Count(a => a.CompanyProfileId == p.CompanyProfileId
                             && a.PeriodYear  == p.Year
                             && a.PeriodMonth == p.Month
                             && a.Status      != "STORNIERT")
            })
            .ToListAsync();

        return Ok(list);
    }

    // GET /api/payroll-perioden/{id}
    [HttpGet("{id}")]
    public async Task<IActionResult> GetPeriode(int id)
    {
        var p = await _db.PayrollPerioden
            .Include(p => p.Snapshots)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (p is null) return NotFound();

        return Ok(new {
            p.Id, p.CompanyProfileId,
            p.Year, p.Month, p.Label,
            PeriodFrom = p.PeriodFrom.ToString("yyyy-MM-dd"),
            PeriodTo   = p.PeriodTo.ToString("yyyy-MM-dd"),
            p.Status,
            p.AbgeschlossenAm, p.AbgeschlossenVon,
            p.CreatedAt,
            p.PdfFooterText,
            SnapshotCount = p.Snapshots.Count,
            FinalCount    = p.Snapshots.Count(s => s.IsFinal)
        });
    }

    // GET /api/payroll-perioden/current?companyProfileId=X&year=Y&month=M
    // Gibt die Periode für den angegebenen Jahr/Monat zurück (oder null wenn nicht angelegt)
    [HttpGet("current")]
    public async Task<IActionResult> GetCurrentPeriode(
        [FromQuery] int companyProfileId,
        [FromQuery] int year,
        [FromQuery] int month)
    {
        var p = await _db.PayrollPerioden
            .Include(p => p.Snapshots)
            .Where(p => p.CompanyProfileId == companyProfileId
                     && p.Year == year
                     && p.Month == month)
            .FirstOrDefaultAsync();

        if (p is null) return Ok(null);

        return Ok(new {
            p.Id, p.CompanyProfileId,
            p.Year, p.Month, p.Label,
            PeriodFrom = p.PeriodFrom.ToString("yyyy-MM-dd"),
            PeriodTo   = p.PeriodTo.ToString("yyyy-MM-dd"),
            p.Status,
            p.AbgeschlossenAm, p.AbgeschlossenVon,
            p.CreatedAt,
            p.PdfFooterText,
            SnapshotCount = p.Snapshots.Count,
            FinalCount    = p.Snapshots.Count(s => s.IsFinal)
        });
    }

    // POST /api/payroll-perioden  – neue Periode anlegen (oder öffnen)
    [HttpPost]
    public async Task<IActionResult> CreatePeriode([FromBody] CreatePeriodeDto dto)
    {
        // Doppel-Check: existiert bereits eine Periode für diesen Monat?
        var existing = await _db.PayrollPerioden
            .FirstOrDefaultAsync(p => p.CompanyProfileId == dto.CompanyProfileId
                                   && p.Year  == dto.Year
                                   && p.Month == dto.Month);
        if (existing is not null)
            return Conflict(new { message = $"Periode {dto.Month}/{dto.Year} existiert bereits.", id = existing.Id });

        // Walter-Vorgabe 20.05.2026: Lohnperiode = IMMER Kalendermonat
        // (1.–Letzter des Monats). Keine Periodenregel-Konfiguration mehr,
        // keine Übergangs-Lohnläufe.
        var plannedFrom = new DateOnly(dto.Year, dto.Month, 1);
        var plannedTo   = new DateOnly(dto.Year, dto.Month, DateTime.DaysInMonth(dto.Year, dto.Month));

        var periode = new PayrollPeriode
        {
            CompanyProfileId = dto.CompanyProfileId,
            Year             = dto.Year,
            Month            = dto.Month,
            PeriodFrom       = plannedFrom,
            PeriodTo         = plannedTo,
            Label            = dto.Label ?? FormatLabel(dto.Year, dto.Month),
            Status           = "offen"
        };
        _db.PayrollPerioden.Add(periode);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetPeriode), new { id = periode.Id },
            new {
                periode.Id, periode.Year, periode.Month, periode.Label,
                PeriodFrom = periode.PeriodFrom.ToString("yyyy-MM-dd"),
                PeriodTo   = periode.PeriodTo.ToString("yyyy-MM-dd"),
                periode.Status
            });
    }

    // POST /api/payroll-perioden/{id}/abschliessen
    // Schliesst die Periode ab: alle Snapshots werden IsFinal=true, keine Korrekturen mehr möglich
    // PATCH /api/payroll-perioden/{id}/bemerkung – Footer-Text der Periode setzen
    public class BemerkungDto { public string? Text { get; set; } }

    [HttpPatch("{id}/bemerkung")]
    public async Task<IActionResult> UpdateBemerkung(int id, [FromBody] BemerkungDto dto)
    {
        var periode = await _db.PayrollPerioden.FindAsync(id);
        if (periode is null) return NotFound("Periode nicht gefunden.");
        periode.PdfFooterText = string.IsNullOrWhiteSpace(dto.Text) ? null : dto.Text.Trim();
        await _db.SaveChangesAsync();
        return Ok(new { periodeId = id, pdfFooterText = periode.PdfFooterText });
    }

    /// <summary>
    /// DELETE /api/payroll-perioden/{id}
    /// Löscht eine Lohnperiode — nur erlaubt wenn:
    ///   • Status = "offen" (nicht provisorisch_abgeschlossen, nicht abgeschlossen)
    ///   • Keine Snapshots (bestätigte Lohnzettel) vorhanden
    ///   • Keine PayrollSaldi mit Status='confirmed' für diese Year/Month/Filiale
    /// Cascade: PayrollPeriodeAudit-Einträge werden mit gelöscht.
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> DeletePeriode(int id, [FromQuery] bool force = false)
    {
        var periode = await _db.PayrollPerioden
            .Include(p => p.Snapshots)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (periode is null) return NotFound(new { error = "Periode nicht gefunden." });

        if (periode.Status != "offen")
            return Conflict(new { error = $"Periode ist nicht mehr offen (Status: {periode.Status}). Erst 'wieder öffnen' / 'zurück an GF', dann löschen." });

        if (periode.Snapshots.Count > 0)
            return Conflict(new { error = $"Es bestehen {periode.Snapshots.Count} bestätigte Lohnzettel für diese Periode. Bitte zuerst alle Lohnzettel wieder öffnen, dann erneut versuchen." });

        // PayrollSaldi für diese Year/Month/Company:
        //   • 'confirmed' Saldi blockieren (HR hat schon Lohn bestätigt) →
        //     nur mit ?force=true mitlöschen.
        //   • 'draft' Saldi werden IMMER mitgelöscht (sind transienter Zustand
        //     und würden sonst als Waisen die Vortrags-Logik des Folgemonats
        //     vergiften — z.B. falsche Vormonats-Werte für Stunden/Saldi).
        var allSaldi = await _db.PayrollSaldos
            .Where(s => s.CompanyProfileId == periode.CompanyProfileId
                     && s.PeriodYear  == periode.Year
                     && s.PeriodMonth == periode.Month)
            .ToListAsync();
        var saldiConfirmed = allSaldi.Count(s => s.Status == "confirmed");
        var saldiDraft     = allSaldi.Count - saldiConfirmed;
        if (saldiConfirmed > 0 && !force)
            return Conflict(new {
                error = $"Es bestehen {saldiConfirmed} bestätigte Lohn-Saldi für diese Periode (zusätzlich {saldiDraft} im Entwurf). Mit ?force=true können sie zusammen mit der Periode gelöscht werden."
            });

        // Audit-Einträge cascade-löschen
        var auditRows = await _db.PayrollPeriodeAudits
            .Where(a => a.PayrollPeriodeId == id)
            .ToListAsync();
        if (auditRows.Count > 0) _db.PayrollPeriodeAudits.RemoveRange(auditRows);

        // Saldi mitlöschen: bei force = alle, sonst nur 'draft'.
        var saldiToDelete = force ? allSaldi : allSaldi.Where(s => s.Status != "confirmed").ToList();
        if (saldiToDelete.Count > 0) _db.PayrollSaldos.RemoveRange(saldiToDelete);
        var saldiDeleted = saldiToDelete.Count;

        // K2 (Walter 29.08.2026): in dieser Periode verrechnete QST-Korrektur-
        // Posten zurück auf OFFEN — sonst zeigten sie auf eine gelöschte
        // Periode und würden nie mehr verrechnet (kein FK, kein Cascade).
        var korrZurueck = await _db.QstKorrekturen
            .Where(k => k.VerrechnetPeriodeId == id && k.Status == "VERRECHNET")
            .ToListAsync();
        foreach (var k in korrZurueck)
        {
            k.Status              = "OFFEN";
            k.VerrechnetPeriodeId = null;
            k.VerrechnetAt        = null;
        }

        // K3 (Walter 29.08.2026): Darlehens-Raten dieser Periode löschen —
        // der Restsaldo lebt wieder auf, GETILGTE Darlehen zurück auf OFFEN.
        var ratenWeg = await (from r in _db.EmployeeDarlehenRaten
                              join d in _db.EmployeeDarlehen on r.DarlehenId equals d.Id
                              where d.CompanyProfileId == periode.CompanyProfileId
                                    && r.PeriodYear == periode.Year
                                    && r.PeriodMonth == periode.Month
                              select new { Rate = r, Darlehen = d }).ToListAsync();
        foreach (var x in ratenWeg)
        {
            _db.EmployeeDarlehenRaten.Remove(x.Rate);
            if (x.Darlehen.Status == "GETILGT") x.Darlehen.Status = "OFFEN";
        }

        var companyProfileId = periode.CompanyProfileId;
        _db.PayrollPerioden.Remove(periode);
        await _db.SaveChangesAsync();

        return Ok(new {
            deletedPeriodeId = id,
            companyProfileId,
            saldiDeleted,
            auditDeleted = auditRows.Count
        });
    }

    /// <summary>
    /// Provisorischer Lohnabschluss durch den Geschäftsführer.
    /// Status: offen → provisorisch_abgeschlossen
    ///   • Voraussetzung: alle aktiven MA der Filiale haben einen Snapshot
    ///     (=Lohnzettel bestätigt).
    ///   • Snapshots werden finalisiert (IsFinal=true) — Lohnzettel sind eingefroren.
    ///   • Audit-Log: PROVISORISCH_ABGESCHLOSSEN.
    ///   • Vorab-PDF wird in Phase 2 hier automatisch generiert + an HR im Posteingang.
    ///
    /// Behält den Endpoint-Namen "abschliessen" für Backward-Compat — die
    /// Semantik hat sich aber geändert: aus "definitiver Abschluss" ist nun
    /// "provisorischer Abschluss" geworden. Definitiver Abschluss hat einen
    /// eigenen Endpoint (definitiv-abschliessen), der von HR aufgerufen wird.
    /// </summary>
    [HttpPost("{id}/abschliessen")]
    public async Task<IActionResult> AbschliessePeriode(int id, [FromBody] AbschliessenDto dto)
    {
        var periode = await _db.PayrollPerioden
            .Include(p => p.Snapshots)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (periode is null) return NotFound(new { message = "Periode nicht gefunden." });
        if (periode.Status == "abgeschlossen")
            return Conflict(new { message = "Periode ist bereits definitiv abgeschlossen." });
        if (periode.Status == "provisorisch_abgeschlossen")
            return Conflict(new { message = "Periode ist bereits provisorisch abgeschlossen." });
        if (periode.Snapshots.Count == 0)
            return BadRequest(new { message = "Keine bestätigten Lohnzettel vorhanden. Bitte zuerst alle Löhne bestätigen." });

        // Vorbedingungen-Check: alle aktiven, nicht-payroll-excluded MA der
        // Filiale die in dieser Periode einen Vertrag hatten müssen einen
        // bestätigten Lohnzettel (Snapshot) haben. MAs ohne Vertrag in der
        // Periode (z.B. Eintritt in Folgeperiode) werden automatisch
        // übersprungen.
        // ContractStartDate / -EndDate sind DateTime, periode.PeriodFrom/To
        // sind DateOnly. EF Core / Npgsql kann DateOnly.FromDateTime() nicht
        // übersetzen — daher Periode-Grenzen vorab in DateTime konvertieren.
        var periodFromDt = periode.PeriodFrom.ToDateTime(TimeOnly.MinValue);
        var periodToDt   = periode.PeriodTo.ToDateTime(TimeOnly.MaxValue);
        // Walter-Vorgabe 31.05.2026: kein IsActive-Filter mehr am Employment
        // (siehe Austrittsmonat-Bug). Einzig massgeblich: Vertrag liegt in der Periode.
        var maMitVertragInPeriode = await _db.Employees
            .Where(e => e.IsActive
                     && !e.IsPayrollExcluded
                     && e.Employments.Any(emp => emp.CompanyProfileId == periode.CompanyProfileId
                                              && emp.ContractStartDate <= periodToDt
                                              && (!emp.ContractEndDate.HasValue
                                                  || emp.ContractEndDate.Value >= periodFromDt)))
            .Select(e => new { e.Id, e.FirstName, e.LastName, e.EmployeeNumber })
            .ToListAsync();
        var bestaetigteIds = periode.Snapshots.Select(s => s.EmployeeId).ToHashSet();
        var fehlend = maMitVertragInPeriode.Where(e => !bestaetigteIds.Contains(e.Id)).ToList();
        if (fehlend.Count > 0)
        {
            var liste = string.Join(", ", fehlend.Take(5).Select(f => $"{f.FirstName} {f.LastName}"));
            if (fehlend.Count > 5) liste += $" + {fehlend.Count - 5} weitere";
            return BadRequest(new {
                message = $"Es sind noch nicht alle Lohnzettel bestätigt. Fehlend: {liste}",
                fehlend = fehlend.Select(f => new { f.Id, f.FirstName, f.LastName, f.EmployeeNumber })
            });
        }

        // Walter-Vorgabe 19.05.2026: IsFinal NICHT mehr beim „An HR senden"
        // setzen — HR muss in der Phase provisorisch_abgeschlossen noch jeden
        // Snapshot einzeln HR-bestätigen können, evtl. zurückziehen, korrigieren.
        // IsFinal wird erst beim DefinitivAbschliessen (= DTA an Bank gesendet)
        // gesetzt. UpdatedAt-Touch reicht hier für den Audit.
        foreach (var snap in periode.Snapshots)
        {
            // payroll_snapshot = timestamp without time zone → Lokalzeit (Walter 04.08.2026)
            snap.UpdatedAt = DateTime.Now;
        }

        periode.Status                       = "provisorisch_abgeschlossen";
        periode.ProvisorischAbgeschlossenAm  = DateTime.UtcNow;
        periode.ProvisorischAbgeschlossenVon = GetUserId();

        await AddAuditAsync(periode.Id, GetUserId(), "PROVISORISCH_ABGESCHLOSSEN", null);
        await _db.SaveChangesAsync();

        // Walter 03.08.2026: hängender Akonto-Zwischenstatus → UEBERSPRUNGEN
        // (Definitiv hat übernommen — kein Lock-Banner mehr).
        await AkontoDefinitivGuard.TryAbandonMidFlightAsync(_db, periode, GetUserId());

        // Vorab-PDF generieren + ins HR-Posteingang ablegen. Schlägt nicht
        // den Periode-Abschluss fehl wenn was schief geht — nur Console-Log.
        await _lohnlaufSvc.TrySendVorabPdfToHrAsync(periode.Id, GetUserId());

        return Ok(new {
            message    = $"Periode '{periode.Label}' provisorisch abgeschlossen. {periode.Snapshots.Count} Lohnzettel finalisiert. Vorab-PDF wurde ins HR-Postfach abgelegt.",
            periodeId  = periode.Id,
            status     = periode.Status,
            finalCount = periode.Snapshots.Count
        });
    }

    /// <summary>
    /// Definitiver Lohnabschluss durch HR.
    /// Status: provisorisch_abgeschlossen → abgeschlossen
    ///   • Setzt AbgeschlossenAm/Von und das Auszahlungsdatum.
    ///   • Audit-Log: DEFINITIV_ABGESCHLOSSEN.
    ///   • DTA-Generierung passiert in Phase 4 (separater Service-Aufruf).
    /// </summary>
    [HttpPost("{id}/definitiv-abschliessen")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> DefinitivAbschliessen(int id, [FromBody] DefinitivAbschliessenDto dto)
    {
        var periode = await _db.PayrollPerioden
            .Include(p => p.Snapshots)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (periode is null) return NotFound(new { message = "Periode nicht gefunden." });
        if (periode.Status != "provisorisch_abgeschlossen")
            return Conflict(new { message = $"Periode ist im Status '{periode.Status}' — definitiver Abschluss nur aus 'provisorisch_abgeschlossen' möglich." });

        // Walter-Vorgabe 20.05.2026: DTA-Versand erst wenn HR ALLE MA bestätigt
        // hat — analog Akonto (Auszahlen nur in HR_FREIGEGEBEN). Defense in
        // depth: das Frontend versteckt den Button schon, hier wird's hart
        // durchgesetzt (verhindert Race / direkten API-Aufruf).
        var nichtHrBestaetigt = periode.Snapshots
            .Count(s => s.Status != "HR_BESTAETIGT" && s.Status != "ABGESCHLOSSEN");
        if (nichtHrBestaetigt > 0)
            return Conflict(new { message = $"Es sind noch {nichtHrBestaetigt} Lohnzettel nicht HR-bestätigt. Bitte zuerst alle Lohnzettel HR-bestätigen, dann den DTA-Versand auslösen." });

        if (!DateOnly.TryParse(dto.Auszahlungsdatum, out var auszahlung))
            return BadRequest(new { message = "Auszahlungsdatum ungültig (Format: YYYY-MM-DD)." });

        periode.Status            = "abgeschlossen";
        periode.AbgeschlossenAm   = DateTime.UtcNow;
        periode.AbgeschlossenVon  = GetUserId();
        periode.Auszahlungsdatum  = auszahlung;

        // Walter-Vorgabe 19.05.2026: JETZT erst Snapshots einfrieren.
        // IsFinal=true + Status=ABGESCHLOSSEN. Alle HR_BESTAETIGT-Snapshots
        // werden zu ABGESCHLOSSEN; falls jemand noch FREIGEGEBEN_GF hat (sollte
        // eigentlich nicht passieren, weil das Frontend den DTA-Klick erst
        // nach allen HR-Bestätigungen erlaubt), ebenfalls auf ABGESCHLOSSEN.
        foreach (var snap in periode.Snapshots)
        {
            snap.Status    = "ABGESCHLOSSEN";
            snap.IsFinal   = true;
            snap.UpdatedAt = DateTime.Now; // Lokalzeit (Walter 04.08.2026)
        }

        await AddAuditAsync(periode.Id, GetUserId(), "DEFINITIV_ABGESCHLOSSEN",
                             $"Auszahlungsdatum: {auszahlung:dd.MM.yyyy}");
        await _db.SaveChangesAsync();

        await AkontoDefinitivGuard.TryAbandonMidFlightAsync(_db, periode, GetUserId());

        // ── Auto-Versand: Lohnzettel pro MA ins persönliche Postfach ──
        // Wirft keine Exceptions raus (Try…) — wenn was schiefgeht wird's
        // im journalctl geloggt; der Definitiv-Abschluss bleibt erfolgreich.
        // Bei Re-Open + erneutem Abschluss werden alte Lohnzettel ersetzt.
        // PDF-Erstellung ist schnell (paar Sekunden für 50 MA), wir warten ab.
        await _lohnlaufSvc.TryDispatchLohnzettelToMaPostfaecherAsync(periode.Id, GetUserId());

        // ── E-Mail-Versand als Hintergrund-Task ──────────────────────────
        // Mit 500ms Delay pro MA + SMTP-Roundtrip dauert das bei 50 MA gut
        // 2 Minuten. Wenn wir hier awaiten, hängt das UI-Modal solange.
        // Fire-and-Forget mit eigener DI-Scope (Service ist Scoped, würde
        // beim Request-Ende aufgeräumt, also brauchen wir frische Instanzen
        // von DbContext und EmailService).
        var periodeIdSnapshot = periode.Id;
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var bgSvc = scope.ServiceProvider.GetRequiredService<HrSystem.Services.LohnlaufService>();
                await bgSvc.TrySendLohnzettelEmailsAsync(periodeIdSnapshot);
                // Lohnausweis-Download-Links an Behörden (Lohnabtretung-Flag) — ebenfalls nur Link, kein PDF-Anhang.
                await bgSvc.TrySendLohnausweisLinksToBehoerdenAsync(periodeIdSnapshot);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[PayrollPeriodeController] Background-Mail-Task fehlgeschlagen: {ex.Message}");
            }
        });

        return Ok(new {
            message   = $"Periode '{periode.Label}' definitiv abgeschlossen. Auszahlungsdatum: {auszahlung:dd.MM.yyyy}. Lohnzettel wurden ins MA-Postfach abgelegt. E-Mail-Benachrichtigungen werden im Hintergrund versendet.",
            periodeId = periode.Id,
            status    = periode.Status
        });
    }

    /// <summary>
    /// HR/Admin gibt die Periode an den GF zurück (z.B. weil Korrekturen nötig).
    /// Status: provisorisch_abgeschlossen → offen
    ///   • Snapshots werden de-finalisiert (IsFinal=false), damit sie wieder
    ///     bearbeitet werden können.
    ///   • Audit-Log: ZURUECK_AN_GF (mit Begründung).
    /// </summary>
    [HttpPost("{id}/zurueck-an-gf")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> ZurueckAnGf(int id, [FromBody] ZurueckAnGfDto dto)
    {
        var periode = await _db.PayrollPerioden
            .Include(p => p.Snapshots)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (periode is null) return NotFound(new { message = "Periode nicht gefunden." });
        if (periode.Status != "provisorisch_abgeschlossen")
            return Conflict(new { message = $"Periode ist im Status '{periode.Status}' — Zurückgeben nur aus 'provisorisch_abgeschlossen' möglich." });

        // Walter-Bugfix 19.05.2026: bei Rückgabe an GF muss auch der per-MA
        // Status sauber zurückgerollt werden. Sonst sieht der GF in der
        // MA-Liste alle Häkchen (FREIGEGEBEN_GF / HR_BESTAETIGT) obwohl die
        // Periode wieder offen ist — und kann nichts mehr „bestätigen", weil
        // alles schon als bestätigt wirkt.
        // Zusätzlich: PayrollSaldo.status zurück auf 'draft', sonst zeigt das
        // Frontend „bereits bestätigt" obwohl der Snapshot BERECHNET ist.
        foreach (var snap in periode.Snapshots)
        {
            snap.IsFinal           = false;
            snap.Status            = "BERECHNET";
            snap.GfFreigegebenAt   = null;
            snap.GfFreigegebenBy   = null;
            snap.HrBestaetigtAt    = null;
            snap.HrBestaetigtBy    = null;
            snap.UpdatedAt         = DateTime.Now; // Lokalzeit (Walter 04.08.2026)
        }
        var saldosToReset = await _db.PayrollSaldos
            .Where(s => s.CompanyProfileId == periode.CompanyProfileId
                     && s.PeriodYear == periode.Year && s.PeriodMonth == periode.Month)
            .ToListAsync();
        foreach (var sld in saldosToReset)
        {
            sld.Status    = "draft";
            sld.UpdatedAt = DateTime.Now; // Lokalzeit (Walter 04.08.2026)
        }

        periode.Status                       = "offen";
        periode.ProvisorischAbgeschlossenAm  = null;
        periode.ProvisorischAbgeschlossenVon = null;

        await AddAuditAsync(periode.Id, GetUserId(), "ZURUECK_AN_GF", dto.Bemerkung);
        await _db.SaveChangesAsync();

        // Walter-Vorgabe 22.05.2026: Beim Zurückgeben an GF wird die Periode wieder
        // editierbar — damit der Snapshot nicht veraltet (z.B. wenn jetzt ein Lohn
        // korrigiert wird), alle Lohnzettel SOFORT frisch rechnen. So gilt immer
        // Brutto = Netto + Abzüge und das Fibu-Journal/DTA stimmen.
        var recomputed = await _snapshotRecompute.RecomputeAsync(periode.CompanyProfileId, periode.Year, periode.Month);

        return Ok(new {
            message   = $"Periode '{periode.Label}' wurde an den Geschäftsführer zurückgegeben.",
            periodeId = periode.Id,
            status    = periode.Status,
            recomputed
        });
    }

    /// <summary>
    /// Admin öffnet eine bereits definitiv abgeschlossene Periode wieder.
    /// Status: abgeschlossen → provisorisch_abgeschlossen
    /// (nicht direkt zurück nach offen, weil die Snapshots schon finalisiert
    /// sind — von provisorisch_abgeschlossen kann HR via "zurueck-an-gf"
    /// weiter zurück, falls nötig.)
    /// </summary>
    [HttpPost("{id}/wieder-oeffnen")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> WiederOeffnen(int id, [FromBody] WiederOeffnenDto dto)
    {
        var periode = await _db.PayrollPerioden
            .Include(p => p.Snapshots)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (periode is null) return NotFound(new { message = "Periode nicht gefunden." });
        if (periode.Status != "abgeschlossen")
            return Conflict(new { message = $"Periode ist im Status '{periode.Status}' — Wiedereröffnung nur aus 'abgeschlossen' möglich." });

        // Walter-Vorgabe 29.08.2026 (ABSOLUT): NUR die JÜNGSTE abgeschlossene
        // Periode der Filiale darf wieder geöffnet werden — sobald ein
        // Folgemonat abgeschlossen ist, ist die Periode endgültig versiegelt
        // (alle Folge-Saldi bauen darauf auf). Änderungen dann NUR noch über
        // den Korrektur-Mechanismus (qst_korrektur etc.).
        var juengereAbgeschlossen = await _db.PayrollPerioden
            .Where(p => p.CompanyProfileId == periode.CompanyProfileId
                        && p.Status == "abgeschlossen"
                        && (p.Year > periode.Year || (p.Year == periode.Year && p.Month > periode.Month)))
            .OrderBy(p => p.Year).ThenBy(p => p.Month)
            .Select(p => new { p.Year, p.Month })
            .FirstOrDefaultAsync();
        if (juengereAbgeschlossen != null)
        {
            return Conflict(new
            {
                error   = "NICHT_JUENGSTE_PERIODE",
                message = $"Nur die jüngste abgeschlossene Periode kann wieder geöffnet werden — " +
                          $"{juengereAbgeschlossen.Month:00}/{juengereAbgeschlossen.Year} ist bereits abgeschlossen und baut auf dieser Periode auf. " +
                          "Rückwirkende Änderungen laufen über den Korrektur-Mechanismus (z.B. QST-Korrektur)."
            });
        }

        // Walter-Vorgabe 19.05.2026: Reset NUR bis zum Zahldatum DTA. Sobald
        // das Auszahlungsdatum erreicht ist, hat die Bank den DTA verarbeitet
        // und die Periode ist betoniert. Notfall-Eingriff danach NUR via Code
        // (direkter DB-Eingriff durch Entwickler).
        if (periode.Auszahlungsdatum.HasValue
            && DateOnly.FromDateTime(DateTime.UtcNow) > periode.Auszahlungsdatum.Value)
        {
            return Conflict(new {
                error   = "PAYOUT_DATE_REACHED",
                message = $"Zahldatum DTA ({periode.Auszahlungsdatum:dd.MM.yyyy}) ist erreicht — Wiedereröffnung nicht mehr möglich. " +
                          "Die Bank hat den DTA verarbeitet, die Periode ist endgültig abgeschlossen."
            });
        }

        periode.Status            = "provisorisch_abgeschlossen";
        periode.AbgeschlossenAm   = null;
        periode.AbgeschlossenVon  = null;
        // Auszahlungsdatum bleibt — falls HR es nochmal definitiv abschliesst,
        // wird es überschrieben.

        // Walter-Bugfix 19.05.2026: Snapshots, die als ABGESCHLOSSEN markiert
        // waren, zurück auf HR_BESTAETIGT (HR-Bestätigungen bleiben erhalten,
        // nur der finale „Versand"-Klick muss neu erfolgen). Wenn HR weiter
        // zurück will, geht's via /zurueck-an-gf → dort wird auf BERECHNET
        // rückgerollt. IsFinal=false damit der Lock greift wie bei einer
        // normalen provisorisch-Periode.
        foreach (var snap in periode.Snapshots)
        {
            if (snap.Status == "ABGESCHLOSSEN")
                snap.Status = "HR_BESTAETIGT";
            snap.IsFinal   = false;
            snap.UpdatedAt = DateTime.Now; // Lokalzeit (Walter 04.08.2026)
        }

        // K2 (Walter 29.08.2026): OFFENE Korrektur-Posten, deren URSPRUNG in
        // dieser Periode liegt, sind mit der Wiedereröffnung obsolet — die
        // Neuberechnung der Periode enthält die rückwirkende QST-Version nun
        // DIREKT. Blieben sie stehen, würde die Differenz später doppelt
        // verrechnet. (Bereits andernorts VERRECHNETE Posten bleiben.)
        var obsoletePosten = await _db.QstKorrekturen
            .Where(k => k.Status == "OFFEN"
                     && k.CompanyProfileId == periode.CompanyProfileId
                     && k.Jahr == periode.Year && k.Monat == periode.Month)
            .ToListAsync();
        if (obsoletePosten.Count > 0) _db.QstKorrekturen.RemoveRange(obsoletePosten);

        await AddAuditAsync(periode.Id, GetUserId(), "WIEDER_GEOEFFNET", dto.Bemerkung);
        await _db.SaveChangesAsync();

        // ── Lohnzettel aus MA-Postfächern entfernen ──
        // Der MA hat den Lohnzettel evtl. schon gesehen. Bei Wieder-Öffnen
        // könnte sich der Betrag ändern → falsche Version aus dem Postfach
        // raus, damit der MA nicht weiterhin die alte Variante einsehen kann.
        // Bei erneutem definitivem Abschluss landet die korrigierte Version
        // automatisch wieder im Postfach.
        await _lohnlaufSvc.TryDeleteLohnzettelFromMaPostfaecherAsync(periode.Id);

        // Walter-Vorgabe 22.05.2026: Wieder-Öffnen macht die Periode editierbar →
        // Snapshots sofort frisch rechnen, damit sie nicht veralten, falls jetzt
        // Korrekturen folgen. Verhindert das Auseinanderlaufen von Snapshot und
        // Live-Rechnung (Fibu-Journal/DTA bleiben konsistent).
        var recomputed = await _snapshotRecompute.RecomputeAsync(periode.CompanyProfileId, periode.Year, periode.Month);

        return Ok(new {
            message   = $"Periode '{periode.Label}' wurde wieder geöffnet (zurück auf provisorisch_abgeschlossen). Lohnzettel wurden aus den MA-Postfächern entfernt.",
            periodeId = periode.Id,
            status    = periode.Status,
            recomputed
        });
    }

    /// <summary>Audit-Log einer Lohnperiode (chronologisch).</summary>
    [HttpGet("{id}/audit")]
    public async Task<IActionResult> GetAuditLog(int id)
    {
        var entries = await _db.PayrollPeriodeAudits
            .Where(a => a.PayrollPeriodeId == id)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new {
                id        = a.Id,
                userName  = a.UserName,
                userId    = a.UserId,
                action    = a.Action,
                bemerkung = a.Bemerkung,
                createdAt = a.CreatedAt
            })
            .ToListAsync();
        return Ok(entries);
    }

    /// <summary>
    /// Helper: legt einen Audit-Eintrag an (User-Name wird denormalisiert
    /// abgelegt, damit die Historie auch nach User-Löschung lesbar bleibt).
    /// </summary>
    private async Task AddAuditAsync(int periodeId, int userId, string action, string? bemerkung)
    {
        var user = await _db.AppUsers.FindAsync(userId);
        var name = user?.Username ?? user?.Email ?? $"User #{userId}";
        _db.PayrollPeriodeAudits.Add(new PayrollPeriodeAudit
        {
            PayrollPeriodeId = periodeId,
            UserId           = userId,
            UserName         = name,
            Action           = action,
            Bemerkung        = bemerkung,
            CreatedAt        = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Aktuelle User-ID aus dem JWT-Token (NameIdentifier-Claim). Security
    /// (Walter-Vorgabe 20.05.2026): Audit- und „abgeschlossen_von"-Felder dürfen
    /// NIE aus dem Request-Body kommen — sonst kann sich jeder als jemand anderes
    /// ausgeben. Ein im Request-Body mitgesendetes UserId-Feld wird ignoriert.
    /// </summary>
    private int GetUserId()
    {
        var v = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(v, out var id) ? id : 0;
    }

    // GET /api/payroll-perioden/{id}/snapshots  – alle Snapshots einer Periode
    [HttpGet("{id}/snapshots")]
    public async Task<IActionResult> GetSnapshots(int id)
    {
        var snaps = await _db.PayrollSnapshots
            .Include(s => s.Employee)
            .Where(s => s.PayrollPeriodeId == id)
            .OrderBy(s => s.Employee!.LastName)
            .ThenBy(s => s.Employee!.FirstName)
            .Select(s => new {
                s.Id,
                s.EmployeeId,
                Name = s.Employee == null ? "" : s.Employee.LastName + " " + s.Employee.FirstName,
                s.Brutto,
                s.Netto,
                s.SvBasisAhv,
                s.SvBasisBvg,
                s.QstBetrag,
                s.ThirteenthAccumulated,
                s.FerienGeldSaldo,
                s.IsFinal,
                s.Status,
                s.GfFreigegebenAt, s.GfFreigegebenBy,
                s.HrBestaetigtAt,  s.HrBestaetigtBy,
                s.KommentarGf,     s.KommentarHr,
                s.CreatedAt,
                s.UpdatedAt
            })
            .ToListAsync();

        return Ok(snaps);
    }

    // GET /api/payroll-perioden/jahresausweis?companyProfileId=X&year=Y&employeeId=Z
    // Aggregiert alle Snapshots eines Mitarbeitenden für den Lohnausweis
    [HttpGet("jahresausweis")]
    public async Task<IActionResult> GetJahresausweis(
        [FromQuery] int companyProfileId,
        [FromQuery] int year,
        [FromQuery] int employeeId)
    {
        var snaps = await _db.PayrollSnapshots
            .Include(s => s.Periode)
            .Where(s => s.CompanyProfileId == companyProfileId
                     && s.EmployeeId == employeeId
                     && s.Periode!.Year == year
                     && s.IsFinal)
            .OrderBy(s => s.Periode!.Month)
            .ToListAsync();

        if (!snaps.Any())
            return Ok(new { year, employeeId, message = "Keine finalisierten Perioden gefunden.", perioden = new object[0] });

        var result = new {
            year,
            employeeId,
            companyProfileId,
            totalBrutto               = snaps.Sum(s => s.Brutto),
            totalNetto                = snaps.Sum(s => s.Netto),
            totalSvBasisAhv           = snaps.Sum(s => s.SvBasisAhv),
            totalSvBasisBvg           = snaps.Sum(s => s.SvBasisBvg),
            totalQstBetrag            = snaps.Sum(s => s.QstBetrag),
            thirteenthAccumulatedDez  = snaps.OrderByDescending(s => s.Periode!.Month).First().ThirteenthAccumulated,
            ferienGeldSaldoDez        = snaps.OrderByDescending(s => s.Periode!.Month).First().FerienGeldSaldo,
            perioden = snaps.Select(s => new {
                periodeId = s.PayrollPeriodeId,
                month     = s.Periode!.Month,
                label     = s.Periode.Label,
                s.Brutto, s.Netto, s.SvBasisAhv, s.SvBasisBvg, s.QstBetrag,
                s.ThirteenthAccumulated, s.FerienGeldSaldo
            })
        };

        return Ok(result);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  HELPER
    // ══════════════════════════════════════════════════════════════════════════

    private static readonly string[] MonthNames = {
        "", "Januar", "Februar", "März", "April", "Mai", "Juni",
        "Juli", "August", "September", "Oktober", "November", "Dezember"
    };

    private static string FormatLabel(int year, int month)
        => $"{MonthNames[month]} {year}";
}

// ── DTOs ──────────────────────────────────────────────────────────────────────

public record CreatePeriodeDto(
    int CompanyProfileId,
    int Year,
    int Month,
    string? Label);

public record AbschliessenDto(int UserId);
public record DefinitivAbschliessenDto(int UserId, string Auszahlungsdatum);
public record ZurueckAnGfDto(int UserId, string? Bemerkung);
public record WiederOeffnenDto(int UserId, string? Bemerkung);
