using HrSystem.Data;
using HrSystem.Models;
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

    public PayrollPeriodeController(AppDbContext db, HrSystem.Services.LohnlaufService lohnlaufSvc, IServiceScopeFactory scopeFactory)
    {
        _lohnlaufSvc = lohnlaufSvc;
        _db = db;
        _scopeFactory = scopeFactory;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  PERIODEN-KONFIGURATION
    // ══════════════════════════════════════════════════════════════════════════

    // GET /api/payroll-perioden/config?companyProfileId=X
    [HttpGet("config")]
    public async Task<IActionResult> GetConfig([FromQuery] int companyProfileId)
    {
        var cfg = await _db.PayrollPeriodeConfigs
            .Where(c => c.CompanyProfileId == companyProfileId)
            .OrderByDescending(c => c.ValidFromYear)
            .ThenByDescending(c => c.ValidFromMonth)
            .FirstOrDefaultAsync();

        if (cfg is null)
            return Ok(null);

        return Ok(new {
            cfg.Id,
            cfg.CompanyProfileId,
            cfg.FromDay,
            cfg.ToDay,
            cfg.ValidFromYear,
            cfg.ValidFromMonth,
            cfg.IsLocked,
            cfg.CreatedAt
        });
    }

    // GET /api/payroll-perioden/config/all?companyProfileId=X  – alle Konfigs (Historie)
    [HttpGet("config/all")]
    public async Task<IActionResult> GetAllConfigs([FromQuery] int companyProfileId)
    {
        var cfgs = await _db.PayrollPeriodeConfigs
            .Where(c => c.CompanyProfileId == companyProfileId)
            .OrderByDescending(c => c.ValidFromYear)
            .ThenByDescending(c => c.ValidFromMonth)
            .Select(c => new {
                c.Id, c.CompanyProfileId, c.FromDay, c.ToDay,
                c.ValidFromYear, c.ValidFromMonth, c.IsLocked, c.CreatedAt
            })
            .ToListAsync();

        return Ok(cfgs);
    }

    // POST /api/payroll-perioden/config  – neue Konfiguration anlegen ODER
    // bestehende noch ungesperrte Config für dasselbe Year/Month aktualisieren.
    [HttpPost("config")]
    public async Task<IActionResult> CreateConfig([FromBody] CreatePeriodeConfigDto dto)
    {
        // Existiert eine Config für genau dieses Year/Month?
        var existing = await _db.PayrollPeriodeConfigs
            .Where(c => c.CompanyProfileId == dto.CompanyProfileId
                     && c.ValidFromYear == dto.ValidFromYear
                     && c.ValidFromMonth == dto.ValidFromMonth)
            .FirstOrDefaultAsync();

        if (existing is not null)
        {
            // Wenn gesperrt → wirklich blockieren (es gibt schon Perioden, die
            // diese Regel referenzieren — Walter müsste sie erst löschen).
            if (existing.IsLocked)
                return Conflict(new { error = "Diese Periodenregel ist gesperrt, weil bereits Lohnperioden damit angelegt sind. Lösche die betroffene(n) Periode(n) zuerst." });

            // Sonst: einfach in-place aktualisieren. Spart eine Duplikat-Zeile
            // mit identischem Year/Month und vermeidet UNIQUE-Konflikte.
            existing.FromDay = dto.FromDay;
            existing.ToDay   = dto.ToDay;
            await _db.SaveChangesAsync();

            return Ok(new {
                existing.Id,
                existing.FromDay,
                existing.ToDay,
                existing.ValidFromYear,
                existing.ValidFromMonth,
                existing.IsLocked,
                updated = true
            });
        }

        // Prüfe ob aktuelle (jüngste) Config gesperrt UND Werte identisch sind
        // → dann gibt's nichts zu tun.
        var current = await _db.PayrollPeriodeConfigs
            .Where(c => c.CompanyProfileId == dto.CompanyProfileId)
            .OrderByDescending(c => c.ValidFromYear)
            .ThenByDescending(c => c.ValidFromMonth)
            .FirstOrDefaultAsync();

        if (current is not null && current.IsLocked
            && current.FromDay == dto.FromDay && current.ToDay == dto.ToDay)
            return BadRequest(new { error = "Die aktuelle Konfiguration ist identisch und gesperrt. Keine Änderung nötig." });

        var cfg = new PayrollPeriodeConfig
        {
            CompanyProfileId = dto.CompanyProfileId,
            FromDay          = dto.FromDay,
            ToDay            = dto.ToDay,
            ValidFromYear    = dto.ValidFromYear,
            ValidFromMonth   = dto.ValidFromMonth,
            IsLocked         = false
        };
        _db.PayrollPeriodeConfigs.Add(cfg);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetConfig), new { companyProfileId = dto.CompanyProfileId },
            new { cfg.Id, cfg.FromDay, cfg.ToDay, cfg.ValidFromYear, cfg.ValidFromMonth, cfg.IsLocked, updated = false });
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  PERIODEN  (konkrete Lohnperioden)
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
                p.Id, p.CompanyProfileId, p.ConfigId,
                p.Year, p.Month, p.Label,
                PeriodFrom = p.PeriodFrom.ToString("yyyy-MM-dd"),
                PeriodTo   = p.PeriodTo.ToString("yyyy-MM-dd"),
                p.IsTransition, p.Status,
                p.AbgeschlossenAm, p.AbgeschlossenVon,
                p.CreatedAt,
                SnapshotCount = p.Snapshots.Count,
                FinalCount    = p.Snapshots.Count(s => s.IsFinal)
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
            p.Id, p.CompanyProfileId, p.ConfigId,
            p.Year, p.Month, p.Label,
            PeriodFrom = p.PeriodFrom.ToString("yyyy-MM-dd"),
            PeriodTo   = p.PeriodTo.ToString("yyyy-MM-dd"),
            p.IsTransition, p.Status,
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
                     && p.Month == month
                     && !p.IsTransition)
            .FirstOrDefaultAsync();

        if (p is null) return Ok(null);

        return Ok(new {
            p.Id, p.CompanyProfileId, p.ConfigId,
            p.Year, p.Month, p.Label,
            PeriodFrom = p.PeriodFrom.ToString("yyyy-MM-dd"),
            PeriodTo   = p.PeriodTo.ToString("yyyy-MM-dd"),
            p.IsTransition, p.Status,
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
        // Doppel-Check: existiert bereits eine normale Periode für diesen Monat?
        var existing = await _db.PayrollPerioden
            .FirstOrDefaultAsync(p => p.CompanyProfileId == dto.CompanyProfileId
                                   && p.Year  == dto.Year
                                   && p.Month == dto.Month
                                   && !p.IsTransition);
        if (existing is not null)
            return Conflict(new { message = $"Periode {dto.Month}/{dto.Year} existiert bereits.", id = existing.Id });

        // Aktuelle Config laden — die für diesen Year/Month GÜLTIGE Config,
        // nicht einfach die neueste. Bei Walter's 1-31→21-20-Wechsel zum
        // Jan 2027 könnte schon ein Eintrag "21-20 ab Jan 2027" existieren,
        // während für Dez 2026 noch die 1-31-Regel greift.
        var cfg = await GetActiveConfigAsync(dto.CompanyProfileId, dto.Year, dto.Month);

        // Fallback (Walter 16.05.2026, Etappe 5f): Lohnperiode = immer
        // Kalendermonat. PayrollPeriodStartDay (Legacy) wird nicht mehr
        // ausgewertet. Default-Config FromDay=1/ToDay=31 nur damit die FK
        // payroll_periode.config_id einen gültigen Eintrag hat.
        if (cfg is null)
        {
            cfg = new PayrollPeriodeConfig
            {
                CompanyProfileId = dto.CompanyProfileId,
                FromDay          = 1,
                ToDay            = 31,
                ValidFromYear    = dto.Year,
                ValidFromMonth   = 1,
                IsLocked         = false
            };
            _db.PayrollPeriodeConfigs.Add(cfg);
            await _db.SaveChangesAsync();
        }

        // ── Lohnperiode = IMMER Kalendermonat (Walter-Vorgabe 15.05.2026,
        //    Akonto-Lohn-Modell). Die frühere Periodenregel + die Übergangs-/
        //    Lücken-Logik (Regelwechsel 21.–20. ↔ 1.–31.) entfällt — neue
        //    Perioden sind ausnahmslos 1.–Letzter des Monats. Bestehende
        //    Alt-Perioden behalten ihre gespeicherten Daten; dieser Pfad
        //    erzeugt nur NEUE Perioden. CalcPeriodDates / ConfigShort bleiben
        //    als toter Code erhalten.
        var plannedFrom = new DateOnly(dto.Year, dto.Month, 1);
        var plannedTo   = new DateOnly(dto.Year, dto.Month, DateTime.DaysInMonth(dto.Year, dto.Month));

        bool isTransition = false;                 // bleibt immer false (Kalendermonat)
        PayrollPeriode? extraTransition = null;    // keine Übergangsperiode mehr nötig
        string? transitionInfo = null;

        var periode = new PayrollPeriode
        {
            CompanyProfileId = dto.CompanyProfileId,
            ConfigId         = cfg.Id,
            Year             = dto.Year,
            Month            = dto.Month,
            PeriodFrom       = plannedFrom,
            PeriodTo         = plannedTo,
            Label            = isTransition
                ? $"{(dto.Label ?? FormatLabel(dto.Year, dto.Month))} (Übergang)"
                : (dto.Label ?? FormatLabel(dto.Year, dto.Month)),
            IsTransition     = isTransition,
            Status           = "offen"
        };
        _db.PayrollPerioden.Add(periode);

        // Config sperren sobald erste Periode angelegt wird
        if (cfg is not null && !cfg.IsLocked)
        {
            cfg.IsLocked = true;
        }

        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetPeriode), new { id = periode.Id },
            new {
                periode.Id, periode.Year, periode.Month, periode.Label,
                PeriodFrom = periode.PeriodFrom.ToString("yyyy-MM-dd"),
                PeriodTo   = periode.PeriodTo.ToString("yyyy-MM-dd"),
                periode.Status,
                periode.IsTransition,
                transition = transitionInfo,
                extraTransitionPeriode = extraTransition == null ? null : new {
                    extraTransition.Id, extraTransition.Year, extraTransition.Month, extraTransition.Label,
                    PeriodFrom = extraTransition.PeriodFrom.ToString("yyyy-MM-dd"),
                    PeriodTo   = extraTransition.PeriodTo.ToString("yyyy-MM-dd"),
                    extraTransition.IsTransition
                }
            });
    }

    /// <summary>
    /// Liefert die aktive Config für die gegebene (Year, Month). Wenn mehrere
    /// Configs existieren (Walter-Szenario: 1-31 ab Jan 2026, 21-20 ab Jan 2027),
    /// die mit dem höchsten ValidFromYear/Month, das noch ≤ (Year, Month) ist.
    /// </summary>
    private async Task<PayrollPeriodeConfig?> GetActiveConfigAsync(int companyProfileId, int year, int month)
    {
        return await _db.PayrollPeriodeConfigs
            .Where(c => c.CompanyProfileId == companyProfileId
                     && (c.ValidFromYear < year
                         || (c.ValidFromYear == year && c.ValidFromMonth <= month)))
            .OrderByDescending(c => c.ValidFromYear)
            .ThenByDescending(c => c.ValidFromMonth)
            .FirstOrDefaultAsync();
    }

    /// <summary>Kurz-Darstellung "21.–20." einer (eventuell rückbezogenen) Config.</summary>
    private static string ConfigShort(PayrollPeriode prevPeriode)
    {
        // Heuristik aus den Datumsgrenzen der Vorperiode: zeigt nur den Tag
        // an, nicht den Monat — reine UI-Beschriftung, nicht für Berechnungen.
        return $"{prevPeriode.PeriodFrom.Day}.–{prevPeriode.PeriodTo.Day}.";
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
    /// Bonus: wenn nach dem Delete keine Periode mehr für dieselbe Config
    /// existiert, wird die Config entsperrt (IsLocked=false) — damit kann
    /// der User den FromDay/ToDay-Bereich wieder anpassen.
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

        var configId         = periode.ConfigId;
        var companyProfileId = periode.CompanyProfileId;
        _db.PayrollPerioden.Remove(periode);
        await _db.SaveChangesAsync();

        // Config entsperren falls keine Periode mehr existiert, die diese Config nutzt.
        bool configUnlocked = false;
        if (configId.HasValue)
        {
            var stillUsed = await _db.PayrollPerioden
                .AnyAsync(p => p.ConfigId == configId.Value);
            if (!stillUsed)
            {
                var cfg = await _db.PayrollPeriodeConfigs.FindAsync(configId.Value);
                if (cfg != null && cfg.IsLocked)
                {
                    cfg.IsLocked = false;
                    await _db.SaveChangesAsync();
                    configUnlocked = true;
                }
            }
        }

        return Ok(new {
            deletedPeriodeId = id,
            companyProfileId,
            configUnlocked,
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
        var maMitVertragInPeriode = await _db.Employees
            .Where(e => e.IsActive
                     && !e.IsPayrollExcluded
                     && e.Employments.Any(emp => emp.IsActive
                                              && emp.CompanyProfileId == periode.CompanyProfileId
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

        // Snapshots finalisieren — Lohnzettel sind ab jetzt eingefroren.
        foreach (var snap in periode.Snapshots)
        {
            snap.IsFinal   = true;
            snap.UpdatedAt = DateTime.UtcNow;
        }

        periode.Status                       = "provisorisch_abgeschlossen";
        periode.ProvisorischAbgeschlossenAm  = DateTime.UtcNow;
        periode.ProvisorischAbgeschlossenVon = dto.UserId;

        await AddAuditAsync(periode.Id, dto.UserId, "PROVISORISCH_ABGESCHLOSSEN", null);
        await _db.SaveChangesAsync();

        // Vorab-PDF generieren + ins HR-Posteingang ablegen. Schlägt nicht
        // den Periode-Abschluss fehl wenn was schief geht — nur Console-Log.
        await _lohnlaufSvc.TrySendVorabPdfToHrAsync(periode.Id, dto.UserId);

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
        var periode = await _db.PayrollPerioden.FindAsync(id);
        if (periode is null) return NotFound(new { message = "Periode nicht gefunden." });
        if (periode.Status != "provisorisch_abgeschlossen")
            return Conflict(new { message = $"Periode ist im Status '{periode.Status}' — definitiver Abschluss nur aus 'provisorisch_abgeschlossen' möglich." });

        if (!DateOnly.TryParse(dto.Auszahlungsdatum, out var auszahlung))
            return BadRequest(new { message = "Auszahlungsdatum ungültig (Format: YYYY-MM-DD)." });

        periode.Status            = "abgeschlossen";
        periode.AbgeschlossenAm   = DateTime.UtcNow;
        periode.AbgeschlossenVon  = dto.UserId;
        periode.Auszahlungsdatum  = auszahlung;

        await AddAuditAsync(periode.Id, dto.UserId, "DEFINITIV_ABGESCHLOSSEN",
                             $"Auszahlungsdatum: {auszahlung:dd.MM.yyyy}");
        await _db.SaveChangesAsync();

        // ── Auto-Versand: Lohnzettel pro MA ins persönliche Postfach ──
        // Wirft keine Exceptions raus (Try…) — wenn was schiefgeht wird's
        // im journalctl geloggt; der Definitiv-Abschluss bleibt erfolgreich.
        // Bei Re-Open + erneutem Abschluss werden alte Lohnzettel ersetzt.
        // PDF-Erstellung ist schnell (paar Sekunden für 50 MA), wir warten ab.
        await _lohnlaufSvc.TryDispatchLohnzettelToMaPostfaecherAsync(periode.Id, dto.UserId);

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

        foreach (var snap in periode.Snapshots)
        {
            snap.IsFinal   = false;
            snap.UpdatedAt = DateTime.UtcNow;
        }

        periode.Status                       = "offen";
        periode.ProvisorischAbgeschlossenAm  = null;
        periode.ProvisorischAbgeschlossenVon = null;

        await AddAuditAsync(periode.Id, dto.UserId, "ZURUECK_AN_GF", dto.Bemerkung);
        await _db.SaveChangesAsync();

        return Ok(new {
            message   = $"Periode '{periode.Label}' wurde an den Geschäftsführer zurückgegeben.",
            periodeId = periode.Id,
            status    = periode.Status
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
        var periode = await _db.PayrollPerioden.FindAsync(id);
        if (periode is null) return NotFound(new { message = "Periode nicht gefunden." });
        if (periode.Status != "abgeschlossen")
            return Conflict(new { message = $"Periode ist im Status '{periode.Status}' — Wiedereröffnung nur aus 'abgeschlossen' möglich." });

        periode.Status            = "provisorisch_abgeschlossen";
        periode.AbgeschlossenAm   = null;
        periode.AbgeschlossenVon  = null;
        // Auszahlungsdatum bleibt — falls HR es nochmal definitiv abschliesst,
        // wird es überschrieben.

        await AddAuditAsync(periode.Id, dto.UserId, "WIEDER_GEOEFFNET", dto.Bemerkung);
        await _db.SaveChangesAsync();

        // ── Lohnzettel aus MA-Postfächern entfernen ──
        // Der MA hat den Lohnzettel evtl. schon gesehen. Bei Wieder-Öffnen
        // könnte sich der Betrag ändern → falsche Version aus dem Postfach
        // raus, damit der MA nicht weiterhin die alte Variante einsehen kann.
        // Bei erneutem definitivem Abschluss landet die korrigierte Version
        // automatisch wieder im Postfach.
        await _lohnlaufSvc.TryDeleteLohnzettelFromMaPostfaecherAsync(periode.Id);

        return Ok(new {
            message   = $"Periode '{periode.Label}' wurde wieder geöffnet (zurück auf provisorisch_abgeschlossen). Lohnzettel wurden aus den MA-Postfächern entfernt.",
            periodeId = periode.Id,
            status    = periode.Status
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

    private static (DateOnly from, DateOnly to) CalcPeriodDates(int startDay, int year, int month)
    {
        if (startDay <= 1)
        {
            var from = new DateOnly(year, month, 1);
            var to   = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
            return (from, to);
        }
        // z.B. startDay=21: Periode 21.02–20.03 für Auszahlung März
        var toDate   = new DateOnly(year, month, startDay - 1);
        int prevYear = month == 1 ? year - 1 : year;
        int prevMonth = month == 1 ? 12 : month - 1;
        int clampedStart = Math.Min(startDay, DateTime.DaysInMonth(prevYear, prevMonth));
        var fromDate = new DateOnly(prevYear, prevMonth, clampedStart);
        return (fromDate, toDate);
    }

    private static readonly string[] MonthNames = {
        "", "Januar", "Februar", "März", "April", "Mai", "Juni",
        "Juli", "August", "September", "Oktober", "November", "Dezember"
    };

    private static string FormatLabel(int year, int month)
        => $"{MonthNames[month]} {year}";
}

// ── DTOs ──────────────────────────────────────────────────────────────────────

public record CreatePeriodeConfigDto(
    int CompanyProfileId,
    int FromDay,
    int ToDay,
    int ValidFromYear,
    int ValidFromMonth);

public record CreatePeriodeDto(
    int CompanyProfileId,
    int Year,
    int Month,
    string? Label);

public record AbschliessenDto(int UserId);
public record DefinitivAbschliessenDto(int UserId, string Auszahlungsdatum);
public record ZurueckAnGfDto(int UserId, string? Bemerkung);
public record WiederOeffnenDto(int UserId, string? Bemerkung);
