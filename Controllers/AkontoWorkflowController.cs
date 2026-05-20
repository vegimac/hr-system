using System.Globalization;
using System.Security.Claims;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Akonto-Workflow (Walter-Vorgabe 16.05.2026, AKONTO-LOHN-PLAN.md Etappe 2).
///
/// 4-Augen-Prinzip in 5 Status-Stufen pro Lohnperiode:
///   OFFEN → IN_BEARBEITUNG_GF → BEI_HR → HR_FREIGEGEBEN → AUSBEZAHLT
///
///   • GF (user-Role mit Branch-Access) startet die Vorbereitung, sichtet
///     pro MA das Akonto-Lohnblatt, gibt jedes einzeln frei und schickt
///     dann an HR.
///   • HR (superuser-Role) kontrolliert, kann mit Notiz an den GF
///     zurückschicken, gibt Final-Freigabe und löst die Auszahlung (DTA) aus.
///   • Sobald AUSBEZAHLT sind alle Akonto-Datensätze immutable.
///
/// Die DTA-/pain.001-Generierung selbst ist Phase 3d und wird hier
/// vorerst als Status-Übergang ohne tatsächlichen Bankfile abgebildet —
/// der Hook für den Iso20022PainService steht in `Auszahlen`, aktuell aber
/// nur als TODO. Bis dahin stösst HR die Bank-Zahlung manuell an.
/// </summary>
[Authorize]
[ApiController]
[Route("api/akonto/workflow")]
public class AkontoWorkflowController : ControllerBase
{
    private readonly AppDbContext           _db;
    private readonly AkontoLaufService      _service;
    private readonly AkontoListePdfService  _listePdf;
    private readonly ILogger<AkontoWorkflowController> _log;

    public AkontoWorkflowController(AppDbContext db, AkontoLaufService service,
                                    AkontoListePdfService listePdf,
                                    ILogger<AkontoWorkflowController> log)
    {
        _db       = db;
        _service  = service;
        _listePdf = listePdf;
        _log      = log;
    }

    // ── DTOs ────────────────────────────────────────────────────────────────

    public record WorkflowStartRequest(int CompanyProfileId, int Year, int Month, string Stichtag);
    public record PeriodRequest(int CompanyProfileId, int Year, int Month);
    // Walter 19.05.2026: Auszahlen erfragt Bank-Ausführungsdatum (ReqdExctnDt).
    public record AuszahlenRequest(int CompanyProfileId, int Year, int Month, string Auszahlungsdatum);
    public record ZurueckRequest(int CompanyProfileId, int Year, int Month, string Kommentar);
    public record KommentarRequest(string? Kommentar);
    public record SyncFixFromSlipRequest(decimal Auszahlungsbetrag);
    public record HrOverrideRequest(decimal NeuerNettoAkonto, string Grund);

    // ── Status-Abfrage ──────────────────────────────────────────────────────

    /// <summary>
    /// Aktueller Workflow-Stand einer Periode (für die UI). Liefert
    /// Periode-Status, Audit-Felder + alle akonto_zahlung-Datensätze mit
    /// MA-Stammdaten-Snapshot (Name, Bank, Adresse) für die GF-Sicht.
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(
        [FromQuery] int companyProfileId,
        [FromQuery] int year,
        [FromQuery] int month)
    {
        if (!await CanAccessBranchAsync(companyProfileId))
            return StatusCode(403, new { error = "Kein Zugriff auf diese Filiale." });

        var periode = await _db.PayrollPerioden
            .FirstOrDefaultAsync(p => p.CompanyProfileId == companyProfileId
                                   && p.Year == year && p.Month == month);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var zahlungen = await _db.AkontoZahlungen
            .Where(z => z.CompanyProfileId == companyProfileId
                     && z.PeriodYear  == year
                     && z.PeriodMonth == month)
            .Join(_db.Employees, z => z.EmployeeId, e => e.Id, (z, e) => new { z, e })
            // Phantom-MA (IsPayrollExcluded) defensiv ausblenden — Walter-Vorgabe
            // 16.05.2026. Auch wenn der Start-Endpoint sie schon ausschliesst,
            // schützt der Filter hier gegen Altdaten aus früheren Läufen.
            .Where(x => !x.e.IsPayrollExcluded)
            .Select(x => new {
                x.z.Id, x.z.EmployeeId, x.e.EmployeeNumber, x.e.FirstName, x.e.LastName,
                x.e.Street, x.e.HouseNumber, x.e.ZipCode, x.e.City,
                x.z.GeschaetzterBrutto, x.z.GeschaetzteAbzuege, x.z.PfaendungAbzug,
                x.z.NettoAkonto, x.z.Status,
                x.z.PayoutDate,
                x.z.GfFreigegebenAt, x.z.GfFreigegebenBy,
                x.z.KommentarGf, x.z.KommentarHr,
                // Vertragsmodell (für HR-Tab-Badge): jüngster aktiver Vertrag in dieser Filiale.
                Modell = _db.Employments
                    .Where(em => em.EmployeeId == x.z.EmployeeId
                              && em.CompanyProfileId == x.z.CompanyProfileId
                              && em.IsActive)
                    .OrderByDescending(em => em.ContractStartDate)
                    .Select(em => em.EmploymentModel)
                    .FirstOrDefault(),
                // EmployeeBankAccount ist versionsbasiert (ValidFrom/ValidTo).
                // „aktiv heute" = ValidFrom <= today AND (ValidTo IS NULL OR ValidTo >= today).
                BankAccountCount = _db.EmployeeBankAccounts.Count(b =>
                       b.EmployeeId == x.z.EmployeeId
                    && b.ValidFrom <= today
                    && (b.ValidTo == null || b.ValidTo >= today)),
            })
            // CLAUDE.md-Konvention: MA-Listen IMMER nach Vorname sortieren, Tie-Break über Nachname.
            .OrderBy(r => r.FirstName)
            .ThenBy(r => r.LastName)
            .ToListAsync();

        return Ok(new {
            akontoStatus       = periode?.AkontoStatus ?? "OFFEN",
            akontoGfStartedAt  = periode?.AkontoGfStartedAt,
            akontoGfStartedBy  = periode?.AkontoGfStartedBy,
            akontoGfSentAt     = periode?.AkontoGfSentAt,
            akontoGfSentBy     = periode?.AkontoGfSentBy,
            akontoHrFreigegebenAt = periode?.AkontoHrFreigegebenAt,
            akontoHrFreigegebenBy = periode?.AkontoHrFreigegebenBy,
            akontoAusbezahltAt    = periode?.AkontoAusbezahltAt,
            akontoAusbezahltBy    = periode?.AkontoAusbezahltBy,
            zahlungen,
            countTotal          = zahlungen.Count,
            countFreigegebenGf  = zahlungen.Count(z => z.Status == "FREIGEGEBEN_GF"),
            countHrBestaetigt   = zahlungen.Count(z => z.Status == "HR_BESTAETIGT"),
            countBerechnet      = zahlungen.Count(z => z.Status == "BERECHNET"),
            countAusbezahlt     = zahlungen.Count(z => z.Status == "AUSBEZAHLT"),
        });
    }

    // ── GF: Start (Akonto vorbereiten) ──────────────────────────────────────

    /// <summary>
    /// Startet die Akonto-Vorbereitung für eine Periode (oder rechnet sie neu).
    /// • Legt die PayrollPeriode an, falls noch nicht existiert.
    /// • Berechnet via AkontoLaufService die akonto_zahlung-Datensätze.
    /// • Bestehende FREIGEGEBEN_GF-Datensätze bleiben unverändert (GF-Freigabe
    ///   geht durch Re-Berechnung NICHT verloren).
    /// • Bestehende BERECHNET-Datensätze werden mit frischen Werten überschrieben.
    /// • Bestehende AUSBEZAHLT-Datensätze → Konflikt (409): Periode ist schon abgeschlossen.
    /// </summary>
    [HttpPost("start")]
    public async Task<IActionResult> Start([FromBody] WorkflowStartRequest req)
    {
        if (!await CanAccessBranchAsync(req.CompanyProfileId))
            return StatusCode(403, new { error = "Kein Zugriff auf diese Filiale." });
        if (!DateOnly.TryParseExact(req.Stichtag, "yyyy-MM-dd",
                                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var stichtag))
            return BadRequest(new { error = "Stichtag-Format: JJJJ-MM-TT." });

        // Periode muss explizit eröffnet sein (Walter-Vorgabe 16.05.2026 —
        // wir legen NICHT mehr automatisch an, sonst entstehen Phantom-Perioden).
        var periode = await _db.PayrollPerioden
            .FirstOrDefaultAsync(p => p.CompanyProfileId == req.CompanyProfileId
                                   && p.Year == req.Year && p.Month == req.Month);
        if (periode is null)
            return StatusCode(409, new {
                error = $"Periode {req.Month:00}/{req.Year} ist nicht eröffnet. "
                      + "Bitte zuerst auf der Lohnperioden-Seite anlegen."
            });

        // Sequenz-Pflicht: keine Periode überspringen. Vor jeder Aktion in
        // Periode N müssen ALLE früheren Perioden derselben Filiale komplett
        // durch sein — sowohl Akonto-Strang (AkontoStatus=AUSBEZAHLT) als
        // auch Definitiv-Strang (status='abgeschlossen'). Lücken würden die
        // Saldo-Vortrags-Kette brechen (L-GAV-konform, Walter-Vorgabe 16.05.2026).
        var sequenceError = await CheckSequenceAsync(req.CompanyProfileId, req.Year, req.Month);
        if (sequenceError != null)
            return StatusCode(409, new { error = sequenceError });

        if (periode.AkontoStatus == "AUSBEZAHLT")
            return StatusCode(409, new { error = "Periode ist bereits AUSBEZAHLT — Storno-Funktion nötig." });
        // Walter-Vorgabe 19.05.2026: HR (admin/superuser) darf während der
        // BEI_HR-Phase neu berechnen — z.B. nach Erfassen eines Vorschusses.
        // Die Re-Berechnung lässt FREIGEGEBEN_GF / HR_BESTAETIGT-Datensätze
        // intakt (siehe Logik weiter unten). GF bleibt während BEI_HR
        // gesperrt — er muss die Periode erst zurückholen lassen.
        var roleClaim = User.FindFirst(ClaimTypes.Role)?.Value ?? "";
        var isHr      = roleClaim == "admin" || roleClaim == "superuser";
        if (periode.AkontoStatus == "HR_FREIGEGEBEN")
            return StatusCode(409, new { error = "Periode ist HR-freigegeben — bitte erst zurückholen lassen." });
        if (periode.AkontoStatus == "BEI_HR" && !isHr)
            return StatusCode(409, new { error = "Periode steht bei HR — GF kann nicht neu berechnen. Bitte erst zurückholen lassen." });

        // 1) Vorschau frisch rechnen
        AkontoLaufService.AkontoVorschauResponse data;
        try
        {
            // Start = bewusster Commit-Pfad (GF bereitet vor) → LGAV-Eintrag persistieren.
            data = await _service.PreviewAsync(req.CompanyProfileId, req.Year, req.Month, stichtag, persistLgav: true);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }

        // 2) Bestehende Datensätze laden
        var existing = await _db.AkontoZahlungen
            .Where(z => z.CompanyProfileId == req.CompanyProfileId
                     && z.PeriodYear == req.Year && z.PeriodMonth == req.Month)
            .ToListAsync();
        var existingByEmp = existing.ToDictionary(z => z.EmployeeId);

        // 3) Pro berechtigtem MA Datensatz upserten — FREIGEGEBEN_GF wird NICHT überschrieben.
        int created = 0, updated = 0, preservedFreigegeben = 0;
        DateOnly payoutDate = !string.IsNullOrWhiteSpace(data.PayoutDate)
            && DateOnly.TryParseExact(data.PayoutDate, "yyyy-MM-dd",
                                      CultureInfo.InvariantCulture, DateTimeStyles.None, out var pd)
            ? pd : stichtag;
        var eligibleEmpIds = data.Rows.Where(r => r.IsEligible && r.NettoAkonto > 0m)
                                      .Select(r => r.EmployeeId).ToHashSet();

        foreach (var r in data.Rows.Where(r => r.IsEligible && r.NettoAkonto > 0m))
        {
            if (existingByEmp.TryGetValue(r.EmployeeId, out var existRec))
            {
                if (existRec.Status == "FREIGEGEBEN_GF")
                {
                    preservedFreigegeben++;
                    continue;   // GF-Freigabe nicht überschreiben
                }
                if (existRec.Status == "AUSBEZAHLT")
                    continue;   // schon ausbezahlt, lassen
                // BERECHNET → frische Werte rein
                existRec.GeschaetzterBrutto = r.GeschaetzterBrutto;
                existRec.GeschaetzteAbzuege = r.GeschaetzteAbzuege;
                existRec.PfaendungAbzug     = r.PfaendungAbzug;
                existRec.NettoAkonto        = r.NettoAkonto;
                existRec.PayoutDate         = payoutDate;
                existRec.UpdatedAt          = DateTime.UtcNow;
                updated++;
            }
            else
            {
                _db.AkontoZahlungen.Add(new AkontoZahlung
                {
                    EmployeeId         = r.EmployeeId,
                    CompanyProfileId   = req.CompanyProfileId,
                    PeriodYear         = req.Year,
                    PeriodMonth        = req.Month,
                    PayoutDate         = payoutDate,
                    GeschaetzterBrutto = r.GeschaetzterBrutto,
                    FeriengeldAnteil   = 0m,
                    GeschaetzteAbzuege = r.GeschaetzteAbzuege,
                    PfaendungAbzug     = r.PfaendungAbzug,
                    NettoAkonto        = r.NettoAkonto,
                    Status             = "BERECHNET",
                    CreatedAt          = DateTime.UtcNow,
                    UpdatedAt          = DateTime.UtcNow,
                });
                created++;
            }
        }

        // 4) Verwaiste Datensätze (MA nicht mehr berechtigt) entfernen — aber NUR BERECHNET.
        int removedStale = 0;
        foreach (var existRec in existing.Where(z => !eligibleEmpIds.Contains(z.EmployeeId) && z.Status == "BERECHNET"))
        {
            _db.AkontoZahlungen.Remove(existRec);
            removedStale++;
        }

        // 5) Periode-Status setzen
        var userId = GetUserId();
        if (periode.AkontoStatus == "OFFEN")
        {
            periode.AkontoStatus       = "IN_BEARBEITUNG_GF";
            periode.AkontoGfStartedAt  = DateTime.UtcNow;
            periode.AkontoGfStartedBy  = userId;
        }

        await _db.SaveChangesAsync();
        _log.LogInformation("[AkontoWorkflow] Start Filiale={CP} {Y}-{M}: created={C} updated={U} preserved={P} removedStale={R}",
                           req.CompanyProfileId, req.Year, req.Month, created, updated, preservedFreigegeben, removedStale);

        return Ok(new {
            akontoStatus = periode.AkontoStatus,
            created, updated, preservedFreigegeben, removedStale,
            totalNetto = data.TotalNetto,
        });
    }

    // ── GF: Refresh (Werte neu berechnen, nicht-freigegebene Blätter) ──────

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] WorkflowStartRequest req)
    {
        // Refresh ist semantisch identisch zu Start solange wir in IN_BEARBEITUNG_GF sind:
        // freigegebene Blätter bleiben unangetastet, BERECHNETe werden neu gerechnet.
        return await Start(req);
    }

    // ── GF: Lohnblatt freigeben / Freigabe zurückziehen ────────────────────

    [HttpPost("freigeben/{id:int}")]
    public async Task<IActionResult> Freigeben(int id, [FromBody] KommentarRequest? body = null)
    {
        var z = await _db.AkontoZahlungen.FirstOrDefaultAsync(x => x.Id == id);
        if (z is null) return NotFound();
        if (!await CanAccessBranchAsync(z.CompanyProfileId))
            return StatusCode(403, new { error = "Kein Zugriff auf diese Filiale." });

        var periode = await _db.PayrollPerioden
            .FirstOrDefaultAsync(p => p.CompanyProfileId == z.CompanyProfileId
                                   && p.Year == z.PeriodYear && p.Month == z.PeriodMonth);
        if (periode is null || periode.AkontoStatus != "IN_BEARBEITUNG_GF")
            return StatusCode(409, new { error = "Freigabe nur möglich solange Periode IN_BEARBEITUNG_GF ist." });
        if (z.Status == "AUSBEZAHLT")
            return StatusCode(409, new { error = "Datensatz bereits AUSBEZAHLT — unveränderlich." });

        z.Status            = "FREIGEGEBEN_GF";
        z.GfFreigegebenAt   = DateTime.UtcNow;
        z.GfFreigegebenBy   = GetUserId();
        if (body?.Kommentar != null) z.KommentarGf = body.Kommentar;
        z.UpdatedAt         = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { z.Id, z.Status, z.GfFreigegebenAt, z.GfFreigegebenBy });
    }

    /// <summary>
    /// FIX/FIX-M-Spezialfall (Walter Regel 3/4, Etappe 5): bei Festlohn-MA wird der
    /// Akonto exakt = AkontoProzentFix × voraussichtliche Definitiv-Auszahlung
    /// gesetzt. Die AkontoLaufService-Vorschätzung für FIX nimmt nur Brutto-
    /// Monatslohn als Proxy — dieser Endpoint korrigiert auf den echten Wert
    /// aus dem PayrollController.Calculate-Slip (vom Frontend geladen).
    ///
    /// UTP/MTP (Regel 5/6) werden NICHT mehr via diesen Endpoint synced —
    /// dort rechnet AkontoLaufService lokal Stunden + Ferien-Pott − SV exakt.
    /// </summary>
    [HttpPost("sync-fix-from-slip/{id:int}")]
    public async Task<IActionResult> SyncFixFromSlip(int id, [FromBody] SyncFixFromSlipRequest req)
    {
        var z = await _db.AkontoZahlungen.FirstOrDefaultAsync(x => x.Id == id);
        if (z is null) return NotFound();
        if (!await CanAccessBranchAsync(z.CompanyProfileId))
            return StatusCode(403, new { error = "Kein Zugriff auf diese Filiale." });
        if (z.Status != "BERECHNET")
            return StatusCode(409, new { error = $"Nur BERECHNET-Datensätze können auto-synced werden (aktuell: {z.Status})." });

        var periode = await _db.PayrollPerioden
            .FirstOrDefaultAsync(p => p.CompanyProfileId == z.CompanyProfileId
                                   && p.Year == z.PeriodYear && p.Month == z.PeriodMonth);
        if (periode is null || periode.AkontoStatus != "IN_BEARBEITUNG_GF")
            return StatusCode(409, new { error = "Sync nur möglich solange Periode IN_BEARBEITUNG_GF ist." });

        // Regel 3/4: Sync NUR für FIX/FIX-M. UTP/MTP haben in AkontoLaufService
        // jetzt die korrekte lokale Berechnung (Stunden + Ferien-Pott).
        var employment = await _db.Employments
            .Where(em => em.EmployeeId == z.EmployeeId
                      && em.CompanyProfileId == z.CompanyProfileId
                      && em.IsActive)
            .OrderByDescending(em => em.ContractStartDate)
            .FirstOrDefaultAsync();
        var model = (employment?.EmploymentModel ?? "").ToUpperInvariant();
        if (model != "FIX" && model != "FIX-M")
            return StatusCode(409, new { error = $"Sync nur für FIX/FIX-M verfügbar (Modell: {model}). UTP/MTP werden lokal in AkontoLaufService berechnet." });

        var profile = await _db.CompanyProfiles.FindAsync(z.CompanyProfileId);
        if (profile is null) return NotFound("Filiale nicht gefunden.");

        // Walter-Vorgabe 18.05.2026: FIX und FIX-M haben ab jetzt getrennte
        // Prozent-Sätze. FIX-M (Manager) liegt höher (Default 90 %) als FIX
        // (Default 80 %), weil Manager planbar hohe Festlöhne haben.
        var prozent = model == "FIX-M"
            ? Math.Clamp(profile.AkontoProzentFixM, 0m, 100m)
            : Math.Clamp(profile.AkontoProzentFix,  0m, 100m);
        var rohWert = req.Auszahlungsbetrag * (prozent / 100m);
        // Auf CHF 10 abrunden (untere Grenze 0). Gleiches Rundungsverhalten
        // wie in AkontoLaufService.BuildRow Schritt 5.
        var nettoAkonto = Math.Floor(rohWert / 10m) * 10m;
        if (nettoAkonto < 0m) nettoAkonto = 0m;

        // Brutto auf den Auszahlungsbetrag setzen (= sinnvolle Referenz für
        // Logs/Reports), Abzüge auf 0 — der Akonto-Display zeigt jetzt nur
        // noch "X% × CHF YYY = CHF ZZZ" und keinen SV-Aufstellung mehr.
        z.GeschaetzterBrutto = Math.Round(req.Auszahlungsbetrag, 2);
        z.GeschaetzteAbzuege = 0m;
        z.NettoAkonto        = nettoAkonto;
        z.UpdatedAt          = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new {
            z.Id, z.NettoAkonto, z.GeschaetzterBrutto, z.GeschaetzteAbzuege,
            employmentModel = model,
            akontoProzent   = prozent,
            auszahlungsbetrag = Math.Round(req.Auszahlungsbetrag, 2),
        });
    }

    [HttpPost("zurueckziehen/{id:int}")]
    public async Task<IActionResult> Zurueckziehen(int id)
    {
        var z = await _db.AkontoZahlungen.FirstOrDefaultAsync(x => x.Id == id);
        if (z is null) return NotFound();
        if (!await CanAccessBranchAsync(z.CompanyProfileId))
            return StatusCode(403, new { error = "Kein Zugriff auf diese Filiale." });

        var periode = await _db.PayrollPerioden
            .FirstOrDefaultAsync(p => p.CompanyProfileId == z.CompanyProfileId
                                   && p.Year == z.PeriodYear && p.Month == z.PeriodMonth);
        if (periode is null || periode.AkontoStatus != "IN_BEARBEITUNG_GF")
            return StatusCode(409, new { error = "Rückzug nur möglich solange Periode IN_BEARBEITUNG_GF ist." });
        if (z.Status != "FREIGEGEBEN_GF")
            return StatusCode(409, new { error = $"Nur FREIGEGEBEN_GF-Datensätze sind rückziehbar (aktuell: {z.Status})." });

        z.Status          = "BERECHNET";
        z.GfFreigegebenAt = null;
        z.GfFreigegebenBy = null;
        z.UpdatedAt       = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { z.Id, z.Status });
    }

    // ── GF: An HR senden ────────────────────────────────────────────────────

    [HttpPost("an-hr-senden")]
    public async Task<IActionResult> AnHrSenden([FromBody] PeriodRequest req)
    {
        if (!await CanAccessBranchAsync(req.CompanyProfileId))
            return StatusCode(403, new { error = "Kein Zugriff auf diese Filiale." });

        var periode = await _db.PayrollPerioden
            .FirstOrDefaultAsync(p => p.CompanyProfileId == req.CompanyProfileId
                                   && p.Year == req.Year && p.Month == req.Month);
        if (periode is null || periode.AkontoStatus != "IN_BEARBEITUNG_GF")
            return StatusCode(409, new { error = "Periode muss IN_BEARBEITUNG_GF sein." });

        // Alle Lohnblätter müssen FREIGEGEBEN_GF sein (BERECHNET-Reste blockieren).
        var offen = await _db.AkontoZahlungen
            .CountAsync(z => z.CompanyProfileId == req.CompanyProfileId
                          && z.PeriodYear == req.Year && z.PeriodMonth == req.Month
                          && z.Status == "BERECHNET");
        if (offen > 0)
            return StatusCode(409, new { error = $"{offen} Lohnblätter noch nicht freigegeben — bitte erst alle bestätigen." });

        periode.AkontoStatus     = "BEI_HR";
        periode.AkontoGfSentAt   = DateTime.UtcNow;
        periode.AkontoGfSentBy   = GetUserId();
        await _db.SaveChangesAsync();
        _log.LogInformation("[AkontoWorkflow] An HR Filiale={CP} {Y}-{M} von User={U}",
                           req.CompanyProfileId, req.Year, req.Month, periode.AkontoGfSentBy);
        return Ok(new { akontoStatus = periode.AkontoStatus });
    }

    // ── HR: Zurück an GF mit Notiz ──────────────────────────────────────────

    [HttpPost("zurueck-an-gf")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> ZurueckAnGf([FromBody] ZurueckRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Kommentar))
            return BadRequest(new { error = "Bitte einen Begründungs-Kommentar mitgeben." });

        var periode = await _db.PayrollPerioden
            .FirstOrDefaultAsync(p => p.CompanyProfileId == req.CompanyProfileId
                                   && p.Year == req.Year && p.Month == req.Month);
        if (periode is null || periode.AkontoStatus != "BEI_HR")
            return StatusCode(409, new { error = "Periode muss BEI_HR sein." });

        // Kommentar an alle aktiven Lohnblätter dranschreiben (so sieht's der GF überall).
        var rows = await _db.AkontoZahlungen
            .Where(z => z.CompanyProfileId == req.CompanyProfileId
                     && z.PeriodYear == req.Year && z.PeriodMonth == req.Month)
            .ToListAsync();
        foreach (var z in rows)
        {
            z.KommentarHr = req.Kommentar;
            z.UpdatedAt   = DateTime.UtcNow;
        }
        // Periode geht zurück in GF-Bearbeitung; GF-Freigaben bleiben erhalten,
        // damit er gezielt nur die problematischen Blätter zurückzieht.
        periode.AkontoStatus     = "IN_BEARBEITUNG_GF";
        periode.AkontoGfSentAt   = null;
        periode.AkontoGfSentBy   = null;
        await _db.SaveChangesAsync();
        _log.LogInformation("[AkontoWorkflow] Zurück an GF Filiale={CP} {Y}-{M}: '{Kommentar}'",
                           req.CompanyProfileId, req.Year, req.Month, req.Kommentar);
        return Ok(new { akontoStatus = periode.AkontoStatus });
    }

    // ── HR: pro-MA HR-Bestätigung (Walter-Vorgabe 17.05.2026) ───────────────
    //
    // 4-Augen-Symmetrie zum GF: HR bestätigt jeden Lohnzettel einzeln. Status
    // pro MA wechselt FREIGEGEBEN_GF → HR_BESTAETIGT. Sobald ALLE MA der
    // Periode HR_BESTAETIGT sind, transitioniert die Periode automatisch
    // BEI_HR → HR_FREIGEGEBEN und der DTA-Button wird im UI frei.
    //
    [HttpPost("hr-bestaetigen/{id:int}")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> HrBestaetigen(int id)
    {
        var z = await _db.AkontoZahlungen.FirstOrDefaultAsync(x => x.Id == id);
        if (z is null) return NotFound();

        var periode = await _db.PayrollPerioden
            .FirstOrDefaultAsync(p => p.CompanyProfileId == z.CompanyProfileId
                                   && p.Year == z.PeriodYear && p.Month == z.PeriodMonth);
        // Walter 19.05.2026: HR-Bestätigung auch im Zwischen-Status
        // HR_FREIGEGEBEN erlauben — solange noch nicht AUSBEZAHLT ist,
        // darf HR einzelne MA noch nachträglich bestätigen (nach einem
        // Zurückziehen / Override). Erst der DTA-Klick sperrt final.
        if (periode is null
            || (periode.AkontoStatus != "BEI_HR" && periode.AkontoStatus != "HR_FREIGEGEBEN"))
            return StatusCode(409, new { error = "HR-Bestätigung nur möglich solange noch nicht ausbezahlt (aktuell: "
                                                + (periode?.AkontoStatus ?? "?") + ")." });
        if (z.Status != "FREIGEGEBEN_GF")
            return StatusCode(409, new { error = $"Lohnblatt muss FREIGEGEBEN_GF sein (aktuell: {z.Status})." });

        z.Status     = "HR_BESTAETIGT";
        z.UpdatedAt  = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // Auto-Transit: wenn alle MA der Periode HR_BESTAETIGT sind,
        // springt die Periode auf HR_FREIGEGEBEN (DTA-Button wird frei).
        var offenCnt = await _db.AkontoZahlungen
            .CountAsync(x => x.CompanyProfileId == z.CompanyProfileId
                          && x.PeriodYear == z.PeriodYear && x.PeriodMonth == z.PeriodMonth
                          && x.Status != "HR_BESTAETIGT" && x.Status != "AUSBEZAHLT");
        if (offenCnt == 0 && periode.AkontoStatus != "HR_FREIGEGEBEN")
        {
            periode.AkontoStatus          = "HR_FREIGEGEBEN";
            periode.AkontoHrFreigegebenAt = DateTime.UtcNow;
            periode.AkontoHrFreigegebenBy = GetUserId();
            await _db.SaveChangesAsync();
        }

        return Ok(new { z.Id, z.Status, periodeStatus = periode.AkontoStatus, offenCnt });
    }

    // Symmetrische Rücknahme: HR_BESTAETIGT zurück auf FREIGEGEBEN_GF.
    // Falls die Periode bereits auf HR_FREIGEGEBEN war (alle MA durch), wird
    // sie wieder auf BEI_HR zurückgesetzt — damit DTA blockiert ist solange
    // mind. ein MA wieder offen ist.
    [HttpPost("hr-zurueckziehen/{id:int}")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> HrZurueckziehen(int id)
    {
        var z = await _db.AkontoZahlungen.FirstOrDefaultAsync(x => x.Id == id);
        if (z is null) return NotFound();

        var periode = await _db.PayrollPerioden
            .FirstOrDefaultAsync(p => p.CompanyProfileId == z.CompanyProfileId
                                   && p.Year == z.PeriodYear && p.Month == z.PeriodMonth);
        if (periode is null)
            return StatusCode(409, new { error = "Periode nicht gefunden." });
        if (periode.AkontoStatus != "BEI_HR" && periode.AkontoStatus != "HR_FREIGEGEBEN")
            return StatusCode(409, new { error = "Rücknahme nur möglich solange noch nicht ausbezahlt." });
        if (z.Status != "HR_BESTAETIGT")
            return StatusCode(409, new { error = $"Lohnblatt ist nicht HR_BESTAETIGT (aktuell: {z.Status})." });

        z.Status    = "FREIGEGEBEN_GF";
        z.UpdatedAt = DateTime.UtcNow;
        // Falls Periode war HR_FREIGEGEBEN: zurück auf BEI_HR.
        if (periode.AkontoStatus == "HR_FREIGEGEBEN")
        {
            periode.AkontoStatus          = "BEI_HR";
            periode.AkontoHrFreigegebenAt = null;
            periode.AkontoHrFreigegebenBy = null;
        }
        await _db.SaveChangesAsync();

        return Ok(new { z.Id, z.Status, periodeStatus = periode.AkontoStatus });
    }

    // ── HR: Pauschal-Freigabe (LEGACY — wird vom neuen pro-MA-Flow ersetzt) ─
    //
    // Der Endpoint bleibt funktional für Rückwärtskompatibilität (z.B. wenn
    // alte Frontend-Versionen ihn noch aufrufen), markiert aber alle
    // freigegebenen MA als HR_BESTAETIGT und setzt die Periode auf
    // HR_FREIGEGEBEN. Das neue UI nutzt stattdessen pro-MA hr-bestaetigen.
    [HttpPost("hr-freigabe")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> HrFreigabe([FromBody] PeriodRequest req)
    {
        var periode = await _db.PayrollPerioden
            .FirstOrDefaultAsync(p => p.CompanyProfileId == req.CompanyProfileId
                                   && p.Year == req.Year && p.Month == req.Month);
        if (periode is null || periode.AkontoStatus != "BEI_HR")
            return StatusCode(409, new { error = "Periode muss BEI_HR sein." });

        // Alle FREIGEGEBEN_GF-Lohnzeilen auf HR_BESTAETIGT setzen.
        var rows = await _db.AkontoZahlungen
            .Where(z => z.CompanyProfileId == req.CompanyProfileId
                     && z.PeriodYear == req.Year && z.PeriodMonth == req.Month
                     && z.Status == "FREIGEGEBEN_GF")
            .ToListAsync();
        var now = DateTime.UtcNow;
        foreach (var z in rows)
        {
            z.Status    = "HR_BESTAETIGT";
            z.UpdatedAt = now;
        }
        periode.AkontoStatus           = "HR_FREIGEGEBEN";
        periode.AkontoHrFreigegebenAt  = now;
        periode.AkontoHrFreigegebenBy  = GetUserId();
        await _db.SaveChangesAsync();
        return Ok(new { akontoStatus = periode.AkontoStatus, countHrBestaetigt = rows.Count });
    }

    // ── HR: Direkt-Korrektur des Netto-Akonto-Betrags pro MA ────────────────
    //
    // Walter-Vorgabe 17.05.2026: HR darf in der BEI_HR-Phase einzelne
    // Akonto-Beträge direkt überschreiben (statt nur "Zurück an GF" zu
    // schicken). Audit: vorheriger Wert + Grund + User + Zeit wird im
    // KommentarHr-Feld konkateniert (kein Schema-Wandel nötig).
    //
    // Erlaubt nur solange die Periode BEI_HR ist. Sobald HR-Freigabe gesetzt
    // (HR_FREIGEGEBEN) oder ausbezahlt (AUSBEZAHLT) ist, sind Korrekturen
    // gesperrt — dann müsste HR via Reopen-Endpoint (Phase 3d) zurück.
    //
    [HttpPost("hr-override/{id:int}")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> HrOverride(int id, [FromBody] HrOverrideRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Grund))
            return BadRequest(new { error = "Bitte einen Begründungs-Kommentar mitgeben." });
        if (req.NeuerNettoAkonto < 0m)
            return BadRequest(new { error = "Netto-Akonto darf nicht negativ sein." });

        var z = await _db.AkontoZahlungen.FirstOrDefaultAsync(x => x.Id == id);
        if (z is null) return NotFound();

        var periode = await _db.PayrollPerioden
            .FirstOrDefaultAsync(p => p.CompanyProfileId == z.CompanyProfileId
                                   && p.Year == z.PeriodYear && p.Month == z.PeriodMonth);
        // Walter 19.05.2026: HR-Override auch in HR_FREIGEGEBEN erlauben.
        // Solange der DTA-Klick nicht gefallen ist (AUSBEZAHLT), darf HR
        // einzelne Beträge nachbessern. Der MA fällt dann wie bisher auf
        // FREIGEGEBEN_GF zurück und die Periode auf BEI_HR.
        if (periode is null
            || (periode.AkontoStatus != "BEI_HR" && periode.AkontoStatus != "HR_FREIGEGEBEN"))
            return StatusCode(409, new { error = "HR-Korrektur nur möglich solange noch nicht ausbezahlt (aktuell: "
                                                + (periode?.AkontoStatus ?? "?") + ")." });
        if (z.Status == "AUSBEZAHLT")
            return StatusCode(409, new { error = "Datensatz bereits AUSBEZAHLT — unveränderlich." });

        // Auf CHF 10 abrunden — gleiches Rundungsverhalten wie bei der
        // Auto-Berechnung in AkontoLaufService.
        var neu = Math.Floor(req.NeuerNettoAkonto / 10m) * 10m;
        var alt = z.NettoAkonto;
        if (neu == alt)
            return Ok(new { z.Id, z.NettoAkonto, unchanged = true });

        // Audit-Eintrag an KommentarHr anhängen (chronologisch, mehrere
        // Korrekturen bleiben sichtbar).
        var userId = GetUserId();
        var user   = await _db.AppUsers.FindAsync(userId);
        var who    = user?.Username ?? user?.Email ?? $"User #{userId}";
        var stamp  = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        var audit  = $"[HR-Korrektur {stamp} {who}] CHF {alt:0.00} → CHF {neu:0.00} · {req.Grund.Trim()}";
        z.KommentarHr = string.IsNullOrWhiteSpace(z.KommentarHr)
                            ? audit
                            : (z.KommentarHr + "\n" + audit);
        z.NettoAkonto = neu;
        // Falls schon HR_BESTAETIGT: zurück auf FREIGEGEBEN_GF, damit HR den
        // korrigierten Wert nochmals bewusst bestätigen muss. Wenn dadurch
        // die Periode aus HR_FREIGEGEBEN fällt, wird sie ebenfalls
        // zurückgenommen (damit DTA blockiert).
        if (z.Status == "HR_BESTAETIGT")
        {
            z.Status = "FREIGEGEBEN_GF";
            if (periode.AkontoStatus == "HR_FREIGEGEBEN")
            {
                periode.AkontoStatus          = "BEI_HR";
                periode.AkontoHrFreigegebenAt = null;
                periode.AkontoHrFreigegebenBy = null;
            }
        }
        z.UpdatedAt   = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _log.LogInformation("[AkontoWorkflow] HR-Override Zahlung={Id}: {Alt} → {Neu} von User={U} ({Grund})",
                           z.Id, alt, neu, userId, req.Grund);

        return Ok(new {
            z.Id,
            altNettoAkonto = alt,
            neuNettoAkonto = neu,
            kommentarHr    = z.KommentarHr,
            updatedAt      = z.UpdatedAt,
        });
    }

    // ── HR: Auszahlen (DTA) ─────────────────────────────────────────────────

    /// <summary>
    /// Letzter Schritt: HR löst die Akonto-Auszahlung aus. Aktuell wird der
    /// Status auf AUSBEZAHLT gesetzt und alle Datensätze eingefroren — die
    /// tatsächliche pain.001-Generierung folgt in Phase 3d (Hook unten als
    /// TODO). Bis dahin stösst HR die Bank-Zahlung manuell an.
    /// </summary>
    [HttpPost("auszahlen")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> Auszahlen([FromBody] AuszahlenRequest req)
    {
        if (!DateOnly.TryParse(req.Auszahlungsdatum, out var auszahlungsdatum))
            return BadRequest(new { error = "Auszahlungsdatum ungültig (Format: YYYY-MM-DD)." });
        if (auszahlungsdatum < DateOnly.FromDateTime(DateTime.UtcNow.Date))
            return BadRequest(new { error = "Auszahlungsdatum darf nicht in der Vergangenheit liegen." });

        var periode = await _db.PayrollPerioden
            .FirstOrDefaultAsync(p => p.CompanyProfileId == req.CompanyProfileId
                                   && p.Year == req.Year && p.Month == req.Month);
        if (periode is null || periode.AkontoStatus != "HR_FREIGEGEBEN")
            return StatusCode(409, new { error = "Periode muss HR_FREIGEGEBEN sein." });

        // Walter-Vorgabe 17.05.2026: AUSBEZAHLT akzeptiert jetzt
        // HR_BESTAETIGT (neuer pro-MA-Flow) UND FREIGEGEBEN_GF (Legacy für
        // alte Datensätze, falls jemand alte Pauschal-Freigabe genutzt hat).
        var rows = await _db.AkontoZahlungen
            .Where(z => z.CompanyProfileId == req.CompanyProfileId
                     && z.PeriodYear == req.Year && z.PeriodMonth == req.Month
                     && (z.Status == "HR_BESTAETIGT" || z.Status == "FREIGEGEBEN_GF"))
            .ToListAsync();
        var userId = GetUserId();
        foreach (var z in rows)
        {
            z.Status    = "AUSBEZAHLT";
            z.UpdatedAt = DateTime.UtcNow;
        }
        periode.AkontoStatus           = "AUSBEZAHLT";
        periode.AkontoAusbezahltAt     = DateTime.UtcNow;
        periode.AkontoAusbezahltBy     = userId;
        periode.AkontoAuszahlungsdatum = auszahlungsdatum;
        await _db.SaveChangesAsync();
        _log.LogInformation("[AkontoWorkflow] Auszahlen Filiale={CP} {Y}-{M}: {Count} Datensätze AUSBEZAHLT",
                           req.CompanyProfileId, req.Year, req.Month, rows.Count);

        // Phase 3d (Walter-Vorgabe 17.05.2026): Sanity-Check ob das DTA-XML
        // generierbar wäre. Wir persistieren das File NICHT (on-demand-Download
        // über GET /api/akonto/workflow/dta) — aber wir wollen sofort wissen
        // ob's pain.001-mässig sauber kompiliert, damit Walter nicht stundenlang
        // glaubt es sei alles ok und erst beim Download merkt dass ein MA keine
        // Bank hat. Bei Problem: 500 mit Klartext + AKONTO-Status bleibt
        // trotzdem AUSBEZAHLT (die Zahlung selbst ist immer noch gültig).
        string? dtaWarning = null;
        try
        {
            await _service.GenerateDtaAsync(req.CompanyProfileId, req.Year, req.Month);
        }
        catch (Exception ex)
        {
            dtaWarning = ex.Message;
            _log.LogWarning("[AkontoWorkflow] DTA-Probe-Fehler: {Msg}", ex.Message);
        }

        return Ok(new {
            akontoStatus    = periode.AkontoStatus,
            countAusbezahlt = rows.Count,
            dtaReady        = dtaWarning is null,
            dtaWarning,
            hinweis = dtaWarning is null
                ? "DTA-File kann über '📥 DTA herunterladen' abgerufen werden."
                : $"DTA-Generierung blockiert: {dtaWarning}",
        });
    }

    /// <summary>
    /// Download des pain.001-XML für den Akonto-Lauf einer Periode.
    /// On-demand generiert aus akonto_zahlung (Status AUSBEZAHLT) — kein
    /// File-Storage. Re-Download via identischem GET. admin/superuser only.
    /// </summary>
    [Authorize(Roles = "admin,superuser")]
    [HttpGet("dta")]
    public async Task<IActionResult> DownloadDta(
        [FromQuery] int companyProfileId,
        [FromQuery] int year,
        [FromQuery] int month)
    {
        try
        {
            var bytes = await _service.GenerateDtaAsync(companyProfileId, year, month);
            var filename = $"Akonto_DTA_{companyProfileId}_{year}-{month:D2}.xml";
            Response.Headers["Content-Disposition"] = $"attachment; filename=\"{filename}\"";
            return File(bytes, "application/xml");
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Akonto-Zahlungsliste als PDF (Walter-Vorgabe 18.05.2026).
    /// Pro Filiale + Periode: alle Akonto-Auszahlungen in tabellarischer
    /// Form als Begleitliste zum DTA und Buchhaltungs-Beleg. On-demand
    /// generiert, Re-Download jederzeit möglich.
    /// </summary>
    [Authorize(Roles = "admin,superuser")]
    [HttpGet("liste-pdf")]
    public async Task<IActionResult> DownloadListePdf(
        [FromQuery] int companyProfileId,
        [FromQuery] int year,
        [FromQuery] int month)
    {
        try
        {
            var bytes = await _listePdf.GenerateAsync(companyProfileId, year, month);
            var filename = $"Akonto_Liste_{companyProfileId}_{year}-{month:D2}.pdf";
            Response.Headers["Content-Disposition"] = $"attachment; filename=\"{filename}\"";
            return File(bytes, "application/pdf");
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Rich-Detail für ein Akonto-Lohnblatt — alles was der GF zum
    /// Plausibilisieren braucht: Stammdaten + Vertrag + Stempelzeiten-Summary +
    /// Absenzen-Liste + Brutto-Berechnung + Abzüge-Breakdown pro SV-Satz +
    /// Pfändungs-/HR-Notizen. Wird vom Frontend on-demand beim MA-Klick geladen.
    /// </summary>
    [HttpGet("lohnblatt/{id:int}")]
    public async Task<IActionResult> GetLohnblatt(int id)
    {
        var z = await _db.AkontoZahlungen.FirstOrDefaultAsync(x => x.Id == id);
        if (z is null) return NotFound();
        if (!await CanAccessBranchAsync(z.CompanyProfileId))
            return StatusCode(403, new { error = "Kein Zugriff auf diese Filiale." });

        var emp = await _db.Employees.Include(e => e.Employments)
                                     .FirstOrDefaultAsync(e => e.Id == z.EmployeeId);
        if (emp is null) return NotFound();

        var employment = emp.Employments
            .Where(em => em.CompanyProfileId == z.CompanyProfileId && em.IsActive)
            .OrderByDescending(em => em.ContractStartDate)
            .FirstOrDefault();

        // Filial-Profil für AkontoProzentFix (Frontend braucht das für die
        // vereinfachte FIX/FIX-M-Anzeige "X% × DefinitivAuszahlung").
        var profile = await _db.CompanyProfiles.FindAsync(z.CompanyProfileId);

        var periodFrom = new DateOnly(z.PeriodYear, z.PeriodMonth, 1);
        var periodTo   = new DateOnly(z.PeriodYear, z.PeriodMonth, DateTime.DaysInMonth(z.PeriodYear, z.PeriodMonth));
        var stichtag   = z.PayoutDate;

        var entries = await _db.EmployeeTimeEntries
            .Where(t => t.EmployeeId == emp.Id
                     && t.EntryDate  >= periodFrom
                     && t.EntryDate  <= stichtag)
            .ToListAsync();
        decimal totalHours = entries.Sum(t => t.TotalHours ?? t.DurationHours ?? 0m);

        var absences = await _db.Absences
            .Where(a => a.EmployeeId == emp.Id
                     && a.DateFrom <= periodTo
                     && a.DateTo   >= periodFrom)
            .OrderBy(a => a.DateFrom)
            .ToListAsync();

        var today = DateOnly.FromDateTime(DateTime.Today);
        var banks = await _db.EmployeeBankAccounts
            .Where(b => b.EmployeeId == emp.Id
                     && b.ValidFrom <= today
                     && (b.ValidTo == null || b.ValidTo >= today))
            .OrderByDescending(b => b.IsHauptbank)
            .Select(b => new { b.Iban, b.BankName, b.IsHauptbank })
            .ToListAsync();

        // SV-Sätze am Stichtag, dedupliziert (latest valid_from gewinnt).
        var allSv = await _db.SocialInsuranceRates
            .Where(r => r.IsActive && !r.OnlyQuellensteuer
                     && r.ValidFrom <= stichtag
                     && (r.ValidTo == null || r.ValidTo >= stichtag))
            .ToListAsync();
        var svRates = allSv
            .GroupBy(r => new { r.Code, r.MinAge, r.MaxAge,
                                EmpModel = r.EmploymentModelCode ?? "",
                                r.BasisType })
            .Select(g => g.OrderByDescending(r => r.ValidFrom).First())
            .ToList();

        int? age = emp.DateOfBirth.HasValue
            ? AgeAtStichtag(emp.DateOfBirth.Value, stichtag)
            : (int?)null;

        var abzuegeDetails = BuildAbzuegeBreakdown(
            z.GeschaetzterBrutto, svRates, employment?.EmploymentModel ?? "", age);

        return Ok(new {
            z.Id, z.EmployeeId,
            employee = new {
                emp.EmployeeNumber, emp.FirstName, emp.LastName,
                emp.Street, emp.HouseNumber, emp.ZipCode, emp.City,
                emp.DateOfBirth, emp.SocialSecurityNumber, age,
            },
            vertrag = employment == null ? null : (object) new {
                employment.EmploymentModel,
                employment.HourlyRate, employment.MonthlySalary, employment.MonthlySalaryFte,
                employment.EmploymentPercentage,
                employment.WeeklyHours, employment.GuaranteedHoursPerWeek,
                contractStartDate = employment.ContractStartDate.ToString("yyyy-MM-dd"),
            },
            banks,
            stempelzeiten = new {
                totalHours = Math.Round(totalHours, 2),
                entryCount = entries.Count,
                fromDate   = entries.Count > 0 ? entries.Min(e => e.EntryDate).ToString("yyyy-MM-dd") : null,
                toDate     = entries.Count > 0 ? entries.Max(e => e.EntryDate).ToString("yyyy-MM-dd") : null,
            },
            absenzen = absences.Select(a => new {
                a.AbsenceType,
                fromDate = a.DateFrom.ToString("yyyy-MM-dd"),
                toDate   = a.DateTo.ToString("yyyy-MM-dd"),
                days     = a.DateTo.DayNumber - a.DateFrom.DayNumber + 1,
                a.HoursCredited, a.Prozent,
            }),
            berechnung = new {
                z.GeschaetzterBrutto, z.GeschaetzteAbzuege, z.PfaendungAbzug, z.NettoAkonto,
                abzuegeDetails,
            },
            periodFrom = periodFrom.ToString("yyyy-MM-dd"),
            periodTo   = periodTo.ToString("yyyy-MM-dd"),
            z.PayoutDate, z.Status,
            z.GfFreigegebenAt, z.GfFreigegebenBy,
            z.KommentarGf, z.KommentarHr,
            // Filial-Defaults für die Akonto-Prozente (Frontend zeigt bei
            // FIX/FIX-M die vereinfachte "X% × DefinitivNetto"-Zeile).
            // Seit Walter-Vorgabe 18.05.2026 getrennt für FIX und FIX-M.
            akontoProzentFix  = profile?.AkontoProzentFix  ?? 80m,
            akontoProzentFixM = profile?.AkontoProzentFixM ?? 90m,
        });
    }

    // Abzüge-Breakdown — gleiche Logik wie AkontoLaufService.ComputeDeductions,
    // aber liefert pro SV-Satz die Basis + Betrag zurück (für die Anzeige im Detail).
    private static List<object> BuildAbzuegeBreakdown(
        decimal brutto, List<SocialInsuranceRate> svRates, string model, int? age)
    {
        var result = new List<object>();
        if (brutto <= 0m) return result;
        foreach (var r in svRates)
        {
            if (!string.IsNullOrEmpty(r.EmploymentModelCode)
                && !r.EmploymentModelCode.Equals(model, StringComparison.OrdinalIgnoreCase))
                continue;
            if (r.MinAge.HasValue && age.HasValue && age.Value < r.MinAge.Value) continue;
            if (r.MaxAge.HasValue && age.HasValue && age.Value > r.MaxAge.Value) continue;

            decimal basis;
            switch (r.BasisType)
            {
                case "bvg_basis":
                {
                    var koord = r.CoordinationDeduction ?? 0m;
                    basis = brutto > koord ? brutto - koord : 0m;
                    break;
                }
                case "coord_deduction":
                    basis = r.CoordinationDeduction ?? 0m;
                    break;
                default:
                {
                    var freibetrag = r.FreibetragMonthly ?? 0m;
                    basis = brutto > freibetrag ? brutto - freibetrag : 0m;
                    break;
                }
            }
            var amount = Math.Round(basis * (r.Rate / 100m), 2);
            if (amount <= 0m) continue;
            result.Add(new
            {
                r.Code, r.Name,
                rate   = r.Rate,
                basis  = Math.Round(basis, 2),
                amount,
            });
        }
        return result;
    }

    private static int AgeAtStichtag(DateTime dob, DateOnly stichtag)
    {
        var d = DateOnly.FromDateTime(dob);
        int age = stichtag.Year - d.Year;
        if (stichtag.Month < d.Month || (stichtag.Month == d.Month && stichtag.Day < d.Day)) age--;
        return age;
    }

    /// <summary>
    /// Älteste noch nicht abgeschlossene Periode der Filiale. Wird vom Frontend
    /// genutzt, um beim Page-Open / Filial-Wechsel automatisch dorthin zu springen
    /// (Walter-Vorgabe 16.05.2026 — keine Lücken erlaubt). Liefert null wenn
    /// alle existierenden Perioden komplett ausbezahlt + abgeschlossen sind
    /// (oder gar keine Periode existiert).
    /// </summary>
    [HttpGet("oldest-open-period")]
    public async Task<IActionResult> GetOldestOpenPeriod([FromQuery] int companyProfileId)
    {
        if (!await CanAccessBranchAsync(companyProfileId))
            return StatusCode(403, new { error = "Kein Zugriff auf diese Filiale." });

        var oldest = await _db.PayrollPerioden
            .Where(p => p.CompanyProfileId == companyProfileId
                     && (p.AkontoStatus != "AUSBEZAHLT" || p.Status != "abgeschlossen"))
            .OrderBy(p => p.Year).ThenBy(p => p.Month)
            .Select(p => new {
                p.Year, p.Month,
                akontoStatus    = p.AkontoStatus,
                definitivStatus = p.Status,
            })
            .FirstOrDefaultAsync();
        return Ok(oldest);
    }

    // ── Inboxen + Pending-Counts (für Sidebar-Badge) ────────────────────────

    /// <summary>
    /// Anzahl Akonto-Läufe, die auf den eingeloggten User warten.
    /// • HR / superuser: alle Perioden mit Status BEI_HR (filialübergreifend)
    /// • GF / user:      Perioden mit Status IN_BEARBEITUNG_GF in seinen Filialen
    /// </summary>
    [HttpGet("pending-counts")]
    public async Task<IActionResult> PendingCounts()
    {
        var role   = User.FindFirst(ClaimTypes.Role)?.Value ?? "";
        var userId = GetUserId();
        if (role == "admin" || role == "superuser")
        {
            int hrInbox = await _db.PayrollPerioden.CountAsync(p => p.AkontoStatus == "BEI_HR");
            return Ok(new { role = "hr", inbox = hrInbox });
        }
        else
        {
            var branchIds = await _db.UserBranchAccesses
                .Where(uba => uba.UserId == userId)
                .Select(uba => uba.CompanyProfileId).ToListAsync();
            int gfInbox = await _db.PayrollPerioden
                .CountAsync(p => p.AkontoStatus == "IN_BEARBEITUNG_GF"
                              && branchIds.Contains(p.CompanyProfileId));
            return Ok(new { role = "gf", inbox = gfInbox });
        }
    }

    /// <summary>HR-Inbox: alle Perioden mit Status BEI_HR (filialübergreifend).</summary>
    [HttpGet("pending-hr")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> PendingHr()
    {
        var list = await _db.PayrollPerioden
            .Where(p => p.AkontoStatus == "BEI_HR")
            .Join(_db.CompanyProfiles, p => p.CompanyProfileId, c => c.Id, (p, c) => new {
                p.Id, p.CompanyProfileId, p.Year, p.Month,
                BranchName     = c.BranchName ?? c.CompanyName,
                RestaurantCode = c.RestaurantCode,
                p.AkontoGfSentAt, p.AkontoGfSentBy,
                CountTotal       = _db.AkontoZahlungen.Count(z => z.CompanyProfileId == p.CompanyProfileId
                                                              && z.PeriodYear == p.Year && z.PeriodMonth == p.Month),
                CountFreigegeben = _db.AkontoZahlungen.Count(z => z.CompanyProfileId == p.CompanyProfileId
                                                              && z.PeriodYear == p.Year && z.PeriodMonth == p.Month
                                                              && z.Status == "FREIGEGEBEN_GF"),
                TotalNetto       = _db.AkontoZahlungen.Where(z => z.CompanyProfileId == p.CompanyProfileId
                                                              && z.PeriodYear == p.Year && z.PeriodMonth == p.Month
                                                              && z.Status == "FREIGEGEBEN_GF")
                                                      .Sum(z => (decimal?)z.NettoAkonto) ?? 0m,
            })
            .OrderBy(r => r.AkontoGfSentAt)
            .ToListAsync();
        return Ok(list);
    }

    // ── Periode zurücksetzen (Admin-Notfall, Walter-Vorgabe 17.05.2026) ────

    public record ResetPeriodeRequest(int CompanyProfileId, int Year, int Month, string Grund);

    /// <summary>
    /// Admin-only: setzt eine laufende oder ausbezahlte Akonto-Periode komplett
    /// auf OFFEN zurück. Konsequenzen:
    ///   • Alle BERECHNET / FREIGEGEBEN_GF / HR_BESTAETIGT - Datensätze
    ///     werden gelöscht (sind nur Vorbereitungswerte).
    ///   • AUSBEZAHLT-Datensätze werden auf STORNIERT umgestempelt
    ///     (Geld ist ja schon geflossen — der Eintrag bleibt als Beleg, der
    ///     STORNIERT-Status verhindert eine Doppelverrechnung im Definitivlauf).
    ///   • payroll_periode.akonto_status → OFFEN, alle Audit-Zeitstempel
    ///     (Started/Sent/HrFreigegeben/Ausbezahlt) auf null.
    ///   • Audit-Eintrag in payroll_periode_audit mit Action=AKONTO_RESET
    ///     plus Grund.
    ///
    /// Erst NACH dem Reset können lohnrelevante Daten dieser Periode wieder
    /// editiert werden (LohnEditLockService sieht dann AkontoStatus=OFFEN).
    /// </summary>
    [Authorize(Roles = "admin")]
    [HttpPost("reset-periode")]
    public async Task<IActionResult> ResetPeriode([FromBody] ResetPeriodeRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Grund))
            return BadRequest(new { error = "Grund für das Zurücksetzen ist erforderlich (wird im Audit gespeichert)." });

        if (!await CanAccessBranchAsync(req.CompanyProfileId))
            return Forbid();

        var periode = await _db.PayrollPerioden
            .FirstOrDefaultAsync(p => p.CompanyProfileId == req.CompanyProfileId
                                    && p.Year == req.Year && p.Month == req.Month);
        if (periode is null)
            return NotFound(new { error = $"Keine payroll_periode für Filiale {req.CompanyProfileId} {req.Year}-{req.Month} gefunden." });

        if (periode.AkontoStatus == "OFFEN")
            return Ok(new { message = "Periode war bereits OFFEN — nichts zu tun.", akontoStatus = periode.AkontoStatus });

        // Walter-Vorgabe 19.05.2026: Reset NUR bis zum Bank-Ausführungsdatum
        // (AkontoAuszahlungsdatum) — sobald das überschritten ist, hat die
        // Bank den DTA verarbeitet und die Periode ist betoniert. Fallback
        // für Alt-Daten ohne das Feld: Klick-Datum AkontoAusbezahltAt.Date.
        if (periode.AkontoStatus == "AUSBEZAHLT")
        {
            var heute  = DateOnly.FromDateTime(DateTime.UtcNow.Date);
            var cutoff = periode.AkontoAuszahlungsdatum
                       ?? (periode.AkontoAusbezahltAt.HasValue
                              ? DateOnly.FromDateTime(periode.AkontoAusbezahltAt.Value.Date)
                              : (DateOnly?)null);
            if (cutoff.HasValue && heute > cutoff.Value)
            {
                return Conflict(new {
                    error   = "PAYOUT_DATE_REACHED",
                    message = $"Akonto-Zahldatum ({cutoff:dd.MM.yyyy}) ist erreicht — Reset nicht mehr möglich. " +
                              "Die Bank hat den DTA inzwischen verarbeitet, die Akonto-Periode ist endgültig abgeschlossen."
                });
            }
        }

        // akonto_zahlung-Aufräumen
        var zahlungen = await _db.AkontoZahlungen
            .Where(z => z.CompanyProfileId == req.CompanyProfileId
                     && z.PeriodYear  == req.Year
                     && z.PeriodMonth == req.Month)
            .ToListAsync();

        int gelöscht = 0, storniert = 0;
        foreach (var z in zahlungen)
        {
            if (z.Status == "AUSBEZAHLT")
            {
                // Geld ist geflossen — Eintrag bleibt als Beleg, aber wird
                // als STORNIERT markiert damit der Definitivlauf ihn nicht
                // als Vorauszahlung verrechnet.
                z.Status = "STORNIERT";
                storniert++;
            }
            else
            {
                _db.AkontoZahlungen.Remove(z);
                gelöscht++;
            }
        }

        // Periode zurücksetzen
        var prev = new
        {
            Status = periode.AkontoStatus,
            GfStartedAt    = periode.AkontoGfStartedAt,
            GfSentAt       = periode.AkontoGfSentAt,
            HrFreigegebenAt= periode.AkontoHrFreigegebenAt,
            AusbezahltAt   = periode.AkontoAusbezahltAt,
        };
        periode.AkontoStatus           = "OFFEN";
        periode.AkontoGfStartedAt      = null; periode.AkontoGfStartedBy      = null;
        periode.AkontoGfSentAt         = null; periode.AkontoGfSentBy         = null;
        periode.AkontoHrFreigegebenAt  = null; periode.AkontoHrFreigegebenBy  = null;
        periode.AkontoAusbezahltAt     = null; periode.AkontoAusbezahltBy     = null;
        periode.AkontoDtaRunId         = null;

        // Audit-Eintrag
        var userName = User.FindFirst(ClaimTypes.Name)?.Value ?? "Unbekannt";
        _db.PayrollPeriodeAudits.Add(new PayrollPeriodeAudit
        {
            PayrollPeriodeId = periode.Id,
            UserId           = GetUserId(),
            UserName         = userName,
            Action           = "AKONTO_RESET",
            Bemerkung        = $"Vorheriger Status: {prev.Status}. Gelöschte Zahlungen: {gelöscht}, stornierte AUSBEZAHLT-Zahlungen: {storniert}. Grund: {req.Grund}",
            CreatedAt        = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        _log.LogWarning("[AkontoWorkflow] RESET Periode CP={CP} {Y}-{M} durch {User} — vorher={Prev}, gelöscht={D}, storniert={S}, Grund={Grund}",
            req.CompanyProfileId, req.Year, req.Month, userName, prev.Status, gelöscht, storniert, req.Grund);

        return Ok(new
        {
            message       = $"Akonto-Periode {req.Month:D2}/{req.Year} zurückgesetzt.",
            akontoStatus  = periode.AkontoStatus,
            gelöscht,
            storniert,
            grund         = req.Grund
        });
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private int GetUserId() =>
        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var v) ? v : 0;

    /// <summary>
    /// admin + superuser sehen alle Filialen; user nur die mit UserBranchAccess.
    /// </summary>
    private async Task<bool> CanAccessBranchAsync(int companyProfileId)
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "";
        if (role == "admin" || role == "superuser") return true;
        var userId = GetUserId();
        if (userId == 0) return false;
        return await _db.UserBranchAccesses
            .AnyAsync(uba => uba.UserId == userId && uba.CompanyProfileId == companyProfileId);
    }

    /// <summary>
    /// Sequenz-Pflicht: prüft ob ältere Perioden derselben Filiale noch nicht
    /// komplett durch sind (Walter-Vorgabe 16.05.2026 — L-GAV-konform).
    /// „Komplett durch" = Akonto-Strang AUSBEZAHLT UND Definitiv-Strang
    /// abgeschlossen. Liefert null wenn alles in Ordnung; sonst eine
    /// Fehler-Meldung mit dem ältesten blockierenden Eintrag.
    /// </summary>
    private async Task<string?> CheckSequenceAsync(int companyProfileId, int year, int month)
    {
        var refMonth = year * 12 + month;
        // Älteste frühere Periode, die noch nicht beide Stufen abgeschlossen hat.
        var blocker = await _db.PayrollPerioden
            .Where(p => p.CompanyProfileId == companyProfileId
                     && (p.Year * 12 + p.Month) < refMonth
                     && (p.AkontoStatus != "AUSBEZAHLT" || p.Status != "abgeschlossen"))
            .OrderBy(p => p.Year).ThenBy(p => p.Month)
            .FirstOrDefaultAsync();
        if (blocker is null) return null;

        var akontoOffen = blocker.AkontoStatus != "AUSBEZAHLT";
        var defOffen    = blocker.Status       != "abgeschlossen";
        string was = (akontoOffen, defOffen) switch
        {
            (true,  true)  => "Akonto-Auszahlung + Definitivlauf stehen aus",
            (true,  false) => "Akonto-Auszahlung steht aus",
            (false, true)  => "Definitivlauf steht aus",
            _              => "noch nicht abgeschlossen",
        };
        return $"Periode {blocker.Month:00}/{blocker.Year} ist noch nicht abgeschlossen "
             + $"({was}). Bitte zuerst diese Periode fertigstellen — Perioden dürfen nicht "
             + "übersprungen werden (Saldo-Vortrags-Kette, L-GAV).";
    }
}
