using System.Security.Claims;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HrSystem.Controllers;

/// <summary>
/// Quellensteuer-Einträge pro Mitarbeiter (mit Historie).
///
/// Walter-Vorgabe 01.08.2026: QST bleibt während Akonto und HR-Kontrolle
/// (provisorisch_abgeschlossen) editierbar — genau dort wird der Ansatz
/// oft noch korrigiert. Gesperrt erst wenn der DEFINITIV-Lauf
/// <c>abgeschlossen</c> ist (DTA erstellt). Dann: ValidFrom vor dem
/// FirstAllowedDate → nicht mehr editieren/löschen, sondern NEUEN Eintrag
/// anlegen (Versionierung, gleiches Soft-Lock wie Verträge).
/// </summary>
[ApiController]
[Route("api/employees/{employeeId:int}/quellensteuer")]
public class EmployeeQuellensteuerController : ControllerBase
{
    private readonly AppDbContext        _db;
    private readonly LohnEditLockService _editLock;
    private readonly ILogger<EmployeeQuellensteuerController> _log;
    private readonly QstKorrekturService _korrektur;
    private readonly QuellensteuerTarifService _tarife;
    public EmployeeQuellensteuerController(AppDbContext db, LohnEditLockService editLock,
        ILogger<EmployeeQuellensteuerController> log, QstKorrekturService korrektur,
        QuellensteuerTarifService tarife)
    {
        _db       = db;
        _editLock = editLock;
        _log      = log;
        _korrektur = korrektur;
        _tarife   = tarife;
    }

    /// <summary>Filiale des MA (jüngster aktiver Vertrag) — null wenn keiner.</summary>
    private Task<int?> GetEmployeeBranchAsync(int employeeId)
        => _db.Employees
            .Where(e => e.Id == employeeId)
            .SelectMany(e => e.Employments)
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.ContractStartDate)
            .Select(x => (int?)x.CompanyProfileId)
            .FirstOrDefaultAsync();

    /// <summary>Soft-Lock wie Verträge: erst ab Definitiv abgeschlossen.</summary>
    private async Task<DateOnly?> GetQstFirstAllowedAsync(int? branchId)
        => branchId.HasValue
            ? await _editLock.GetFirstAllowedDateForContractsAsync(branchId.Value)
            : null;

    /// <summary>
    /// VERWENDUNGSBASIERTE Sperre (Walter-Vorgabe 29.08.2026, ABSOLUT —
    /// docs/qst-korrektur-konzept.md): eine Version ist eingefroren, wenn
    /// sie in mindestens einem Lohn einer DEFINITIV abgeschlossenen Periode
    /// tatsächlich verwendet wurde (Snapshot existiert und Gültigkeit
    /// überlappt den Monat). Präziser als der frühere Datums-Cutoff: eine
    /// Version, die in einem abgeschlossenen Monat OHNE Lohn galt, bleibt
    /// frei; Admin-Wiedereröffnung (nur jüngste Periode) hebt die Sperre
    /// selbstheilend auf, weil der Monat nicht mehr «abgeschlossen» ist.
    /// </summary>
    private async Task<List<DateOnly>> GetAbgeschlosseneLohnMonateAsync(int employeeId)
        => (await (from s in _db.PayrollSnapshots
                   join p in _db.PayrollPerioden on s.PayrollPeriodeId equals p.Id
                   where s.EmployeeId == employeeId
                         && s.Status != "STORNIERT"
                         && p.Status == "abgeschlossen"
                   select new { p.Year, p.Month })
                  .Distinct()
                  .ToListAsync())
            .Select(x => new DateOnly(x.Year, x.Month, 1))
            .OrderBy(d => d)
            .ToList();

    private static bool VersionUeberlapptMonat(EmployeeQuellensteuer q, DateOnly monatsStart)
    {
        var monatsEnde = monatsStart.AddMonths(1).AddDays(-1);
        return q.ValidFrom <= monatsEnde && (q.ValidTo == null || q.ValidTo >= monatsStart);
    }

    private static bool IstVersionVerwendet(EmployeeQuellensteuer q, List<DateOnly> abgeschlosseneMonate)
        => abgeschlosseneMonate.Any(m => VersionUeberlapptMonat(q, m));

    private static DateOnly? VerwendetBis(EmployeeQuellensteuer q, List<DateOnly> abgeschlosseneMonate)
        => abgeschlosseneMonate.Where(m => VersionUeberlapptMonat(q, m))
            .Select(m => (DateOnly?)m).LastOrDefault();

    private static object MapToDto(EmployeeQuellensteuer q, DateOnly? firstAllowed,
        List<DateOnly>? abgeschlosseneMonate = null,
        List<string>? herleitungDiff = null) => new
    {
        // K4.1: Feld-Diff zur chronologischen Vorversion (History-Anzeige).
        herleitungDiff = herleitungDiff ?? new List<string>(),
        q.Id, q.EmployeeId,
        validFrom = q.ValidFrom.ToString("yyyy-MM-dd"),
        validTo   = q.ValidTo?.ToString("yyyy-MM-dd"),
        q.Steuerkanton, q.SteuerkantonName,
        q.QstGemeinde, q.QstGemeindeBfsNr,
        q.TarifvorschlagQst, q.TarifCode, q.TarifBezeichnung,
        q.AnzahlKinder, q.Kirchensteuer, q.QstCode,
        q.SpezielBewilligt, q.Kategorie, q.Prozentsatz,
        q.MindestlohnSatzbestimmung,
        q.PartnerEmployeeId, q.PartnerEinkommenVon, q.PartnerEinkommenBis,
        q.ArbeitsortKanton, q.WeitereBeschaftigungen,
        q.GesamtpensumWeitereAg, q.GesamteinkommenWeitereAg,
        // Anderer Arbeitgeber des MA (Walter 25.08.2026) — volle Adresse.
        q.WeitereAgName, q.WeitereAgStrasse, q.WeitereAgPlz,
        q.WeitereAgOrt, q.WeitereAgKanton, q.WeitereAgLand,
        q.Halbfamilie, q.WohnsitzAusland, q.Wohnsitzstaat, q.AdresseAusland,
        q.LivesInKonkubinat, q.HasJointParentalCare,
        q.PaysAlimonyAdultChildren, q.HasHigherIncomeThanPartner,
        q.IsGrenzgaenger, q.IsWochenaufenthalter,
        // Walter 21.08.2026: Tarifbestätigung als Beleg-Doku.
        q.DokumentId,
        q.CreatedAt, q.UpdatedAt,
        // Verwendungsbasiert (Walter 29.08.2026): true wenn die Version in
        // einem DEFINITIV abgeschlossenen Lohn tatsächlich verwendet wurde.
        // Fallback (ohne Monatsliste): alter Datums-Cutoff.
        inLohnVerwendet = abgeschlosseneMonate != null
            ? IstVersionVerwendet(q, abgeschlosseneMonate)
            : (firstAllowed.HasValue && q.ValidFrom < firstAllowed.Value),
        verwendetBis = abgeschlosseneMonate != null
            ? VerwendetBis(q, abgeschlosseneMonate)?.ToString("yyyy-MM")
            : null
    };

    /// <summary>
    /// Liefert die IDs aller Mitarbeiter mit einem per heute aktiven QST-Eintrag.
    /// Frontend nutzt das für den Spezialfilter „Quellensteuerpflichtig".
    /// Walter-Vorgabe 18.05.2026 — analog /api/employee-bank-accounts/active-employee-ids.
    /// </summary>
    // GET /api/employees/0/quellensteuer/active-ids
    // (Route ignoriert die {employeeId} aus der Klassen-Route — Pattern wie active-employee-ids bei Bank.)
    [HttpGet("/api/employee-quellensteuer/active-employee-ids")]
    public async Task<IActionResult> GetActiveQstEmployeeIds(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to   = null)
    {
        // Default ohne Parameter: gültig heute (rückwärtskompatibel — z.B.
        // Spezialfilter „QST-pflichtig" auf der MA-Liste).
        // Mit Periode (Lohnlauf-Kontext, Walter 18.05.2026): mindestens ein
        // QST-Eintrag muss IRGENDWO innerhalb der Periode aktiv gewesen sein —
        // also Überlappung: ValidFrom <= to AND (ValidTo IS NULL OR ValidTo >= from).
        var refFrom = from ?? DateOnly.FromDateTime(DateTime.Today);
        var refTo   = to   ?? refFrom;
        var ids = await _db.EmployeeQuellensteuer
            .Where(q => q.ValidFrom <= refTo
                     && (q.ValidTo == null || q.ValidTo >= refFrom))
            .Select(q => q.EmployeeId)
            .Distinct()
            .ToListAsync();
        return Ok(ids);
    }

    // GET /api/employees/{employeeId}/quellensteuer
    // Gibt alle QST-Einträge eines Mitarbeiters zurück (neueste zuerst)
    [HttpGet]
    public async Task<IActionResult> GetAll(int employeeId)
    {
        var branchId     = await GetEmployeeBranchAsync(employeeId);
        var firstAllowed = await GetQstFirstAllowedAsync(branchId);

        var entries = await _db.EmployeeQuellensteuer
            .Where(q => q.EmployeeId == employeeId)
            .OrderByDescending(q => q.ValidFrom)
            .ToListAsync();

        var verwendet = await GetAbgeschlosseneLohnMonateAsync(employeeId);

        // K4.1: Diff jeder Version zur chronologischen Vorversion.
        var chron = entries.OrderBy(q => q.ValidFrom).ThenBy(q => q.Id).ToList();
        var diffById = new Dictionary<int, List<string>>();
        for (int i = 0; i < chron.Count; i++)
            diffById[chron[i].Id] = i == 0
                ? new List<string>()
                : HerleitungDiff(chron[i - 1].HerleitungJson, chron[i].HerleitungJson);

        return Ok(entries.Select(q =>
            MapToDto(q, firstAllowed, verwendet, diffById.GetValueOrDefault(q.Id))).ToList());
    }

    // GET /api/employees/{employeeId}/quellensteuer/current?date=2026-04-01
    // Gibt den für ein Datum gültigen QST-Eintrag zurück
    [HttpGet("current")]
    public async Task<IActionResult> GetCurrent(int employeeId, [FromQuery] DateOnly? date)
    {
        var refDate = date ?? DateOnly.FromDateTime(DateTime.Today);
        var entry = await _db.EmployeeQuellensteuer
            .Where(q => q.EmployeeId == employeeId
                     && q.ValidFrom <= refDate
                     && (q.ValidTo == null || q.ValidTo >= refDate))
            .OrderByDescending(q => q.ValidFrom)
            .FirstOrDefaultAsync();
        return Ok(entry); // null wenn keiner gefunden
    }

    /// <summary>
    /// Walter-Vorgabe 14.06.2026: serverseitiger Tarifvorschlag für einen
    /// neuen QST-Eintrag. Berechnet aus Stammdaten (Zivilstand, Religion,
    /// Wohnkanton, Familie-Kinder) den passenden Tarif + Kinderzahl +
    /// Kirchensteuer und prüft gegen die offizielle ESTV-Tariftabelle
    /// (mit Fallback wenn die exakte Kombi fehlt). Frontend nutzt das
    /// beim Öffnen des „+ Neuer Eintrag"-Modals.
    ///
    /// Stichtag default = heute, kann via ?date= überschrieben werden
    /// (z.B. wenn der Eintrag in der Zukunft starten soll).
    /// </summary>
    [HttpGet("vorschlag")]
    public async Task<IActionResult> GetVorschlag(int employeeId, [FromQuery] DateOnly? date,
        [FromServices] QstTarifVorschlagService service)
    {
        // try/catch mit Klartext-Meldung (Walter 13.07.2026): der Endpoint
        // lieferte einen nackten 500 — das QST-Modal zeigt die message jetzt
        // im Hinweis an, damit die Ursache diagnostizierbar ist.
        try
        {
            var stichtag = date ?? DateOnly.FromDateTime(DateTime.Today);
            var result   = await service.BerechneAsync(employeeId, stichtag);
            if (result == null) return NotFound(new { error = "MA_NICHT_GEFUNDEN" });

            // K4.5 Grün/Rot-Status (Automatik-Perimeter, Schulung Kap. 10):
            // GRUEN = eindeutige Daten + eindeutige Regel. ROT = Handlung
            // nötig — jede Lücke nennt den für ihre Dimension definierten
            // Fallback (steckt bereits in den Warnings der Herleitung).
            // Kein Orange, kein pauschales ROT→A0.
            var luecken = new List<string>(result.Warnings);
            if (result.AbklaerungNoetig)
                luecken.Add("Gemischter Konkubinatsfall — Tarif mit der QST-Behörde abklären (kein automatischer Vorschlag).");
            if (string.IsNullOrWhiteSpace(result.Steuerkanton))
                luecken.Add("QST-Kanton unklar — Lohn kann vorbereitet werden, aber KEINE definitive QST-Abrechnung, bis der Kanton geklärt ist.");
            result = result with
            {
                Status  = luecken.Count == 0 ? "GRUEN" : "ROT",
                Luecken = luecken
            };
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                error = "VORSCHLAG_FEHLGESCHLAGEN",
                message = ex.GetBaseException().Message
            });
        }
    }

    // GET /api/employees/{employeeId}/quellensteuer/{id}
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int employeeId, int id)
    {
        var entry = await _db.EmployeeQuellensteuer
            .FirstOrDefaultAsync(q => q.Id == id && q.EmployeeId == employeeId);
        if (entry is null) return NotFound();
        return Ok(entry);
    }

    /// <summary>
    /// Walter-Vorgabe 21.08.2026: Tarifbestätigung der Steuerbehörde als
    /// Beleg-Dokument an eine QST-Version hängen (oder lösen, dokumentId=null).
    /// Reiner Beleg — bewusst OHNE Lohn-Edit-Lock (ändert keine Tarif-Daten),
    /// analog Family-Member-/Permit-History-Dokument-PATCH.
    /// PATCH /api/employees/{employeeId}/quellensteuer/{id}/dokument
    /// </summary>
    [HttpPatch("{id:int}/dokument")]
    public async Task<IActionResult> SetDokument(int employeeId, int id, [FromBody] QstDokumentDto dto)
    {
        var entry = await _db.EmployeeQuellensteuer
            .FirstOrDefaultAsync(q => q.Id == id && q.EmployeeId == employeeId);
        if (entry is null) return NotFound();

        if (dto.DokumentId.HasValue)
        {
            var dokOk = await _db.EmployeeDokumente
                .AnyAsync(d => d.Id == dto.DokumentId.Value && d.EmployeeId == employeeId);
            if (!dokOk)
                return BadRequest(new { error = "DOKUMENT_INVALID",
                    message = "Das verlinkte Dokument gehört nicht zu diesem Mitarbeiter." });
        }

        entry.DokumentId = dto.DokumentId;
        entry.UpdatedAt  = DateTime.Now;
        await _db.SaveChangesAsync();
        return Ok(new { id = entry.Id, dokumentId = entry.DokumentId });
    }

    public class QstDokumentDto { public int? DokumentId { get; set; } }

    /// <summary>
    /// Behördenbewilligung Kinderabzug Tarif A (Walter 29.08.2026, analog
    /// QST-Befreiung): «Speziell bewilligt» ist NUR mit hinterlegter
    /// Verfügung der Steuerbehörde erlaubt (DokumentId Pflicht, Dokument
    /// muss dem MA gehören). Liefert null wenn alles ok, sonst das
    /// BadRequest-Resultat.
    /// </summary>
    private async Task<IActionResult?> ValidateBewilligungAsync(EmployeeQuellensteuer dto, int employeeId)
    {
        if (dto.SpezielBewilligt && dto.DokumentId == null)
            return BadRequest(new
            {
                error   = "BEWILLIGUNG_DOKUMENT_FEHLT",
                message = "«Kinderabzug behördlich bewilligt (A1–A9)» braucht die Verfügung der Steuerbehörde als Beleg — " +
                          "Dokument zuerst im Dokumente-Tab beim MA ablegen und hier auswählen."
            });
        if (dto.DokumentId.HasValue)
        {
            var dokOk = await _db.EmployeeDokumente
                .AnyAsync(d => d.Id == dto.DokumentId.Value && d.EmployeeId == employeeId);
            if (!dokOk)
                return BadRequest(new { error = "DOKUMENT_INVALID",
                    message = "Das verlinkte Dokument gehört nicht zu diesem Mitarbeiter." });
        }
        return null;
    }

    // POST /api/employees/{employeeId}/quellensteuer
    // Neuen QST-Eintrag anlegen; schliesst vorherigen Eintrag automatisch ab
    [HttpPost]
    public async Task<IActionResult> Create(int employeeId, [FromBody] EmployeeQuellensteuer dto,
        [FromQuery] string? korrekturGrund = null, [FromQuery] bool nurHistorie = false)
    {
        // Prozentsatz/Medianlohn nie negativ (Walter 12.08.2026).
        if (dto.Prozentsatz < 0 || dto.MindestlohnSatzbestimmung < 0)
            return BadRequest(new { error = "NEGATIVER_WERT", message = "Prozentsatz und Medianlohn dürfen nicht negativ sein." });

        // Behördenbewilligung Kinderabzug Tarif A (Walter 29.08.2026, analog
        // QST-Befreiung): «Speziell bewilligt» NUR mit verknüpfter Verfügung.
        var bewCheck = await ValidateBewilligungAsync(dto, employeeId);
        if (bewCheck != null) return bewCheck;

        // K1 KORREKTUR-WEG (Walter 29.08.2026, docs/qst-korrektur-konzept.md):
        // Rückwirkende Versionen über DEFINITIV abgeschlossene Perioden sind
        // erlaubt — aber NUR mit Pflicht-Grund. Die abgeschlossenen Löhne
        // bleiben eingefroren; das System erzeugt qst_korrektur-Posten
        // (Verrechnung im Folgemonat-Lohnlauf, K2). Ohne Grund → 409 mit
        // eigenem Fehlercode, das Frontend fragt den Grund nach.
        var branchId     = await GetEmployeeBranchAsync(employeeId);
        var firstAllowed = await GetQstFirstAllowedAsync(branchId);
        bool istRueckwirkend = firstAllowed.HasValue && dto.ValidFrom < firstAllowed.Value;
        // «Nur Historie nachtragen» (Walter 04.09.2026): die Version dokumentiert
        // einen Zeitraum, dessen Löhne nicht in OneCrew gerechnet wurden (z.B.
        // frühere Adresse im Kanton LU) — keine Korrektur-Posten, kein Grund.
        if (nurHistorie) istRueckwirkend = false;
        // K4.2 Auto-Anlass (Bauplan Punkt 2): der 409-Grund-Zwang wird erst
        // NACH dem Herleitungs-Snapshot geprüft — erkennt der Server eine
        // Differenz zur Vorversion, schreibt er den Grund selbst (weiter
        // unten). Nur ohne erkennbare Änderung wird nachgefragt.

        // GLEICHES Gültig-ab wie bestehende(r) Eintrag/Einträge → ÜBERSCHREIBEN
        // statt Dublette (Walter-Vorgabe 19.08.2026, Fall Gazale: 2× «1.9. bis …»):
        // alle Versionen mit identischem Startdatum entfernen — der neue Eintrag
        // ersetzt sie. Der Soft-Lock oben schützt bereits abgerechnete Perioden.
        var gleicheStart = await _db.EmployeeQuellensteuer
            .Where(q => q.EmployeeId == employeeId && q.ValidFrom == dto.ValidFrom)
            .ToListAsync();
        if (gleicheStart.Count > 0)
            _db.EmployeeQuellensteuer.RemoveRange(gleicheStart);

        // ANDERES (späteres) Gültig-ab → vorherigen offenen Eintrag abschliessen
        // (ValidTo = neues Gültig-ab − 1 Tag)
        var previous = await _db.EmployeeQuellensteuer
            .Where(q => q.EmployeeId == employeeId && q.ValidTo == null
                     && q.ValidFrom != dto.ValidFrom)
            .OrderByDescending(q => q.ValidFrom)
            .FirstOrDefaultAsync();
        if (previous != null && previous.ValidFrom < dto.ValidFrom)
            previous.ValidTo = dto.ValidFrom.AddDays(-1);

        dto.Id         = 0;
        dto.EmployeeId = employeeId;
        dto.CreatedAt  = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified);
        dto.UpdatedAt  = dto.CreatedAt;
        // ValidTo NIE vom Client (Walter 12.08.2026): ein QST-Enddatum
        // entsteht nur systemisch (Folge-Eintrag kappt den Vorgänger,
        // Umzug/Kantonswechsel, Bewilligungswechsel/Befreiung).
        dto.ValidTo = null;
        // HISTORISCHE Version (Walter 04.09.2026): liegt bereits eine spätere
        // Version vor, endet die neue systemisch am Tag vor deren Beginn —
        // sonst gäbe es zwei offene, überlappende Versionen.
        var naechste = await _db.EmployeeQuellensteuer.AsNoTracking()
            .Where(q => q.EmployeeId == employeeId && q.ValidFrom > dto.ValidFrom)
            .OrderBy(q => q.ValidFrom).Select(q => (DateOnly?)q.ValidFrom).FirstOrDefaultAsync();
        if (naechste.HasValue) dto.ValidTo = naechste.Value.AddDays(-1);

        // Steuerkanton/Gemeinde/BFS IMMER aus der Wohnadresse des MA
        // (Walter 12.08.2026): die QST folgt der easy@work-geführten
        // Hauptadresse — Client-Werte werden ignoriert (keine manuelle
        // Abweichung = keine falsche Steuermeldung). Die Spalten bleiben
        // als eingefrorene Historie pro Gültigkeits-Version erhalten.
        await ApplyWohnadresseAsync(dto, employeeId);

        // Kirchensteuer IMMER aus der Konfession des MA (Walter 12.08.2026):
        // nur Anzeige im Modal, Client-Wert wird ignoriert. Der Y/N-Suffix im
        // QST-Code wird passend nachgezogen.
        await ApplyKirchensteuerAsync(dto, employeeId);

        // Konkubinat IMMER aus dem Familie-Tab (Walter 25.08.2026,
        // docs/konkubinat-qst-konzept.md): ist ein Konkubinatspartner erfasst,
        // werden Konkubinat-Häkchen + Einkommensfrage von dort übernommen —
        // Client-Werte werden ignoriert. Ohne K-Partner bleibt der Client-Wert
        // (Alt-Fälle ohne Familien-Eintrag).
        await ApplyKonkubinatAsync(dto, employeeId);

        // Wochenaufenthalt aus der Wohnsituation (Walter 28.08.2026): ist beim
        // MA eine Zusatzadresse vom Typ «Wochenaufenthalt» erfasst, ist das die
        // QUELLE — der Client-Wert wird überschrieben. Ohne solche Adresse
        // bleibt der Client-Wert (Alt-Fälle).
        await ApplyWochenaufenthaltAsync(dto, employeeId);

        // K4.1: komplette Herleitungsbasis server-seitig einfrieren.
        dto.HerleitungJson = await BuildHerleitungSnapshotAsync(dto, employeeId);

        // K4.2 Auto-Anlass: rückwirkend + kein Grund → Diff zur Vorversion.
        // Erkennt der Server WAS sich geändert hat, schreibt er den Grund
        // selbst; sonst bleibt der 409 (Frontend fragt nach).
        if (istRueckwirkend && string.IsNullOrWhiteSpace(korrekturGrund))
        {
            var vorversion = await _db.EmployeeQuellensteuer.AsNoTracking()
                .Where(q => q.EmployeeId == employeeId && q.ValidFrom <= dto.ValidFrom)
                .OrderByDescending(q => q.ValidFrom).ThenByDescending(q => q.Id)
                .FirstOrDefaultAsync();
            var autoDiff = HerleitungDiff(vorversion?.HerleitungJson, dto.HerleitungJson);
            if (autoDiff.Count > 0)
            {
                korrekturGrund = "Automatisch erkannt: " + string.Join(" · ", autoDiff.Take(4))
                    + (autoDiff.Count > 4 ? $" (+{autoDiff.Count - 4} weitere)" : "");
            }
            else
            {
                return Conflict(new
                {
                    error            = "KORREKTUR_GRUND_NOETIG",
                    message          = $"'Gültig ab {dto.ValidFrom:dd.MM.yyyy}' liegt in einer definitiv abgeschlossenen Lohnperiode. " +
                                       "Rückwirkende Erfassung ist möglich — bitte den Korrektur-Grund angeben " +
                                       "(z.B. «Heirat verspätet gemeldet»). Die abgeschlossenen Löhne bleiben unverändert; " +
                                       "die Differenz wird als QST-Korrektur im nächsten Lohnlauf verrechnet.",
                    firstAllowedDate = firstAllowed!.Value.ToString("yyyy-MM-dd")
                });
            }
        }

        _db.EmployeeQuellensteuer.Add(dto);
        await _db.SaveChangesAsync();

        // K1: bei rückwirkender Erfassung die Korrektur-Posten rechnen.
        object? korrekturen = null;
        if (istRueckwirkend)
        {
            var actor = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
                        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var erg = await _korrektur.ErzeugeKorrekturenAsync(dto, korrekturGrund!.Trim(), actor);
            korrekturen = new
            {
                anzahl = erg.Anzahl,
                totalDifferenz = erg.TotalDifferenz,
                vorjahr = erg.Vorjahr,
                posten = erg.Posten
            };
        }

        var result = MapToDto(dto, firstAllowed);
        return Ok(new { eintrag = result, korrekturen });
    }

    // PUT /api/employees/{employeeId}/quellensteuer/{id}
    public sealed class BeendenDto { public DateOnly ValidTo { get; set; } public string? Grund { get; set; } }

    /// <summary>
    /// Offene QST-Version per Datum beenden (Walter 04.09.2026): Heirat mit
    /// CH/C-Partner → QST endet per Monatsende der Heirat (KS 45). Systemisch
    /// gesetztes Enddatum wie bei Folge-Eintrag/Umzug — PUT ignoriert ValidTo.
    /// Gesperrt, wenn ein definitiv abgeschlossener Lohnmonat NACH dem neuen
    /// Enddatum die Version noch verwendet hat.
    /// </summary>
    [HttpPost("{id:int}/beenden")]
    public async Task<IActionResult> Beenden(int employeeId, int id, [FromBody] BeendenDto dto)
    {
        var entry = await _db.EmployeeQuellensteuer.FirstOrDefaultAsync(q => q.Id == id && q.EmployeeId == employeeId);
        if (entry is null) return NotFound();
        if (entry.ValidTo != null)
            return Conflict(new { error = "QST_ABGESCHLOSSEN", message = $"Diese QST-Version ist bereits per {entry.ValidTo:dd.MM.yyyy} beendet." });
        if (dto.ValidTo < entry.ValidFrom)
            return BadRequest(new { error = "ENDE_VOR_BEGINN", message = "Das Enddatum liegt vor dem Beginn der Version." });

        var verwendetIn = await GetAbgeschlosseneLohnMonateAsync(employeeId);
        var nachEnde = verwendetIn.Where(m => m > new DateOnly(dto.ValidTo.Year, dto.ValidTo.Month, 1)).ToList();
        if (nachEnde.Count > 0)
            return Conflict(new
            {
                error = "LOHN_EDIT_LOCKED",
                message = $"Nach dem {dto.ValidTo:dd.MM.yyyy} gibt es definitiv abgeschlossene Löhne mit dieser QST-Version (bis {nachEnde.Last():MM.yyyy}). "
                        + "Die Version kann nicht rückwirkend beendet werden — bitte über eine QST-Korrektur lösen."
            });

        entry.ValidTo = dto.ValidTo;
        await _db.SaveChangesAsync();
        _log.LogInformation("[QST] Version {Id} von MA {Emp} per {Bis} beendet ({Grund})", id, employeeId, dto.ValidTo, dto.Grund);
        return Ok(new { ok = true, id, validTo = dto.ValidTo.ToString("yyyy-MM-dd") });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int employeeId, int id, [FromBody] EmployeeQuellensteuer dto)
    {
        // Prozentsatz/Medianlohn nie negativ (Walter 12.08.2026).
        if (dto.Prozentsatz < 0 || dto.MindestlohnSatzbestimmung < 0)
            return BadRequest(new { error = "NEGATIVER_WERT", message = "Prozentsatz und Medianlohn dürfen nicht negativ sein." });

        var entry = await _db.EmployeeQuellensteuer
            .FirstOrDefaultAsync(q => q.Id == id && q.EmployeeId == employeeId);
        if (entry is null) return NotFound();

        // ABGESCHLOSSENE Version = unveränderbar (Walter 12.08.2026, gleiche
        // Logik wie Verträge): hat der Eintrag ein Enddatum, ist er Historie —
        // Änderungen laufen IMMER über einen neuen Eintrag.
        if (entry.ValidTo != null)
        {
            return Conflict(new
            {
                error   = "QST_ABGESCHLOSSEN",
                message = $"Diese QST-Version ({entry.ValidFrom:dd.MM.yyyy} – {entry.ValidTo:dd.MM.yyyy}) ist abgeschlossen und kann nicht mehr geändert werden. Bitte einen neuen Eintrag erfassen."
            });
        }

        // Soft-Lock: Edit tabu erst nach Definitiv-Abschluss (DTA). Davor
        // (inkl. HR-Kontrolle) korrigierbar. Danach: neuen Eintrag anlegen.
        var branchId     = await GetEmployeeBranchAsync(employeeId);
        var firstAllowed = await GetQstFirstAllowedAsync(branchId);
        var verwendetIn  = await GetAbgeschlosseneLohnMonateAsync(employeeId);
        if (IstVersionVerwendet(entry, verwendetIn))
        {
            var bis = VerwendetBis(entry, verwendetIn);
            return Conflict(new
            {
                error            = "LOHN_EDIT_LOCKED",
                message          = $"Diese QST-Version wurde in definitiv abgeschlossenen Löhnen verwendet (bis {bis:MM.yyyy}) und ist eingefroren. " +
                                   "Änderungen laufen über eine NEUE Version — rückwirkend via Korrektur-Grund (die Differenzen werden automatisch als QST-Korrektur verrechnet).",
                firstAllowedDate = firstAllowed?.ToString("yyyy-MM-dd")
            });
        }

        // Behördenbewilligung Kinderabzug Tarif A (Walter 29.08.2026):
        // «Speziell bewilligt» NUR mit verknüpfter Verfügung (analog Befreiung).
        var bewCheckUpd = await ValidateBewilligungAsync(dto, employeeId);
        if (bewCheckUpd != null) return bewCheckUpd;

        entry.ValidFrom                  = dto.ValidFrom;
        // entry.ValidTo bleibt UNANGETASTET (Walter 12.08.2026): das Enddatum
        // wird nur systemisch gesetzt (Folge-Eintrag/Umzug/Befreiung) — ein
        // Client-Wert wird ignoriert.
        // Wohnort-Kette server-autoritativ (Walter 12.08.2026) — siehe Create.
        await ApplyWohnadresseAsync(entry, employeeId);
        entry.TarifvorschlagQst          = dto.TarifvorschlagQst;
        entry.TarifCode                  = dto.TarifCode;
        entry.TarifBezeichnung           = dto.TarifBezeichnung;
        entry.AnzahlKinder               = dto.AnzahlKinder;
        entry.QstCode                    = dto.QstCode;
        // Kirchensteuer server-autoritativ aus der Konfession (Walter
        // 12.08.2026) — Client-Wert ignoriert, Y/N-Suffix im QstCode
        // wird mitgezogen (siehe Create).
        await ApplyKirchensteuerAsync(entry, employeeId);
        entry.SpezielBewilligt           = dto.SpezielBewilligt;
        // Beleg-Dokument (Verfügung/Tarifbestätigung) — das Modal sendet den
        // bestehenden Wert mit, PATCH …/dokument bleibt der Alternativ-Weg.
        entry.DokumentId                 = dto.DokumentId;
        entry.Kategorie                  = dto.Kategorie;
        entry.Prozentsatz                = dto.Prozentsatz;
        entry.MindestlohnSatzbestimmung  = dto.MindestlohnSatzbestimmung;
        entry.PartnerEmployeeId          = dto.PartnerEmployeeId;
        entry.PartnerEinkommenVon        = dto.PartnerEinkommenVon;
        entry.PartnerEinkommenBis        = dto.PartnerEinkommenBis;
        entry.ArbeitsortKanton           = dto.ArbeitsortKanton;
        entry.WeitereBeschaftigungen     = dto.WeitereBeschaftigungen;
        entry.GesamtpensumWeitereAg      = dto.GesamtpensumWeitereAg;
        entry.GesamteinkommenWeitereAg   = dto.GesamteinkommenWeitereAg;
        // Anderer Arbeitgeber des MA (Walter 25.08.2026) — volle Adresse.
        entry.WeitereAgName              = dto.WeitereAgName;
        entry.WeitereAgStrasse           = dto.WeitereAgStrasse;
        entry.WeitereAgPlz               = dto.WeitereAgPlz;
        entry.WeitereAgOrt               = dto.WeitereAgOrt;
        entry.WeitereAgKanton            = dto.WeitereAgKanton;
        entry.WeitereAgLand              = dto.WeitereAgLand;
        entry.Halbfamilie                = dto.Halbfamilie;
        entry.WohnsitzAusland            = dto.WohnsitzAusland;
        entry.Wohnsitzstaat              = dto.Wohnsitzstaat;
        entry.AdresseAusland             = dto.AdresseAusland;

        // Tarif-relevante Stammdaten (für Anmeldung & Tarifbestimmung).
        // Sind nicht im MA-Stamm — gehören hier hin, weil sie sich mit
        // Lebenslagen ändern und über ValidFrom/ValidTo historisiert werden.
        entry.LivesInKonkubinat          = dto.LivesInKonkubinat;
        entry.HasJointParentalCare       = dto.HasJointParentalCare;
        entry.PaysAlimonyAdultChildren   = dto.PaysAlimonyAdultChildren;
        entry.HasHigherIncomeThanPartner = dto.HasHigherIncomeThanPartner;
        entry.IsGrenzgaenger             = dto.IsGrenzgaenger;
        entry.IsWochenaufenthalter       = dto.IsWochenaufenthalter;

        // Konkubinat IMMER aus dem Familie-Tab (Walter 25.08.2026) — analog
        // Wohnadresse/Kirchensteuer, siehe ApplyKonkubinatAsync.
        await ApplyKonkubinatAsync(entry, employeeId);
        await ApplyWochenaufenthaltAsync(entry, employeeId);

        // K4.1: Herleitungsbasis neu einfrieren (auch bei Korrektur der
        // offenen Version bleibt der Snapshot aktuell).
        entry.HerleitungJson = await BuildHerleitungSnapshotAsync(entry, employeeId);

        entry.UpdatedAt                  = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified);

        await _db.SaveChangesAsync();
        return Ok(entry);
    }

    /// <summary>
    /// Konkubinats-Flags server-autoritativ aus dem Familie-Tab (Walter
    /// 25.08.2026, docs/konkubinat-qst-konzept.md): ist ein Konkubinatspartner
    /// erfasst, ist der Familie-Tab die QUELLE — das QST-Modal zeigt die zwei
    /// Checkboxen nur noch gesperrt an. Ohne K-Partner bleiben die Client-
    /// Werte (Rückwärtskompatibilität für Alt-Erfassungen).
    /// </summary>
    private async Task ApplyKonkubinatAsync(EmployeeQuellensteuer entry, int employeeId)
    {
        var kp = await _db.EmployeeFamilyMembers.AsNoTracking()
            .Where(f => f.EmployeeId == employeeId
                     && f.MemberType == "Konkubinatspartner"
                     && f.DateOfDeath == null)
            .OrderByDescending(f => f.Id)
            .Select(f => new { f.MaHatHoeheresEinkommen })
            .FirstOrDefaultAsync();
        if (kp == null) return;
        entry.LivesInKonkubinat          = true;
        entry.HasHigherIncomeThanPartner = kp.MaHatHoeheresEinkommen == true;
    }

    /// <summary>
    /// Wochenaufenthalt server-autoritativ aus der Wohnsituation (Walter
    /// 28.08.2026): QUELLE ist die Zusatzadresse vom Typ «Wochenaufenthalt»
    /// (employee_address). Existiert eine solche → IsWochenaufenthalter=true
    /// (das QST-Modal zeigt die Checkbox nur noch gesperrt). Ohne Adresse
    /// bleibt der Client-Wert (Alt-Fälle ohne erfasste Aufenthaltsadresse).
    /// Der QST-KANTON bleibt davon unberührt — er hängt IMMER am
    /// Hauptwohnsitz (ApplyWohnadresseAsync), nie am Wochenaufenthaltsort.
    /// </summary>
    private async Task ApplyWochenaufenthaltAsync(EmployeeQuellensteuer entry, int employeeId)
    {
        var hatWaAdresse = await _db.EmployeeAddresses.AsNoTracking()
            .AnyAsync(a => a.EmployeeId == employeeId && a.AddressType == "Wochenaufenthalt");
        if (hatWaAdresse) entry.IsWochenaufenthalter = true;
    }

    // DELETE /api/employees/{employeeId}/quellensteuer/{id}[?force=true]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int employeeId, int id, [FromQuery] bool force = false)
    {
        var entry = await _db.EmployeeQuellensteuer
            .FirstOrDefaultAsync(q => q.Id == id && q.EmployeeId == employeeId);
        if (entry is null) return NotFound();

        // Abgeschlossene Version = Historie, nie löschen (Walter 12.08.2026).
        // AUSNAHME (Walter 21.08.2026): Admin darf mit ?force=true auch
        // Historie-Einträge löschen (Fehlerfassungen/Testdaten im Testjahr
        // 2026). Die Lohnlauf-Sperre unten gilt WEITERHIN für alle — auch
        // force bypasst keine definitiv abgeschlossene Periode (CLAUDE.md:
        // kein Rollen-Bypass beim LohnEditLock).
        if (entry.ValidTo != null && !(force && User.IsInRole("admin")))
        {
            return Conflict(new
            {
                error   = "QST_ABGESCHLOSSEN",
                message = $"Diese QST-Version ({entry.ValidFrom:dd.MM.yyyy} – {entry.ValidTo:dd.MM.yyyy}) ist abgeschlossen und kann nicht gelöscht werden."
                    + (User.IsInRole("admin") ? " Als Admin kannst du das Löschen erzwingen." : "")
            });
        }

        // Soft-Lock: Löschen erst nach Definitiv-Abschluss gesperrt.
        var branchId     = await GetEmployeeBranchAsync(employeeId);
        var firstAllowed = await GetQstFirstAllowedAsync(branchId);
        var verwendetIn  = await GetAbgeschlosseneLohnMonateAsync(employeeId);
        if (IstVersionVerwendet(entry, verwendetIn))
        {
            var bis = VerwendetBis(entry, verwendetIn);
            return Conflict(new
            {
                error            = "LOHN_EDIT_LOCKED",
                message          = $"Diese QST-Version wurde in definitiv abgeschlossenen Löhnen verwendet (bis {bis:MM.yyyy}) und kann nicht gelöscht werden.",
                firstAllowedDate = firstAllowed?.ToString("yyyy-MM-dd")
            });
        }

        _db.EmployeeQuellensteuer.Remove(entry);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Kantonskürzel → deutscher Kantonsname (für Meldung/Anzeige).</summary>
    /// <summary>
    /// Steuerkanton/Gemeinde/BFS aus der WOHNADRESSE des MA ableiten (Walter
    /// 12.08.2026): einzige Quelle ist die easy@work-geführte Hauptadresse
    /// (employee.canton_code/city/zip_code, BFS via Ortschaftsverzeichnis).
    /// </summary>
    /// <summary>
    /// Hauptadresse des MA, wie sie AM STICHTAG galt (Walter 04.09.2026: «wenn
    /// ich eine QST mit Gültig-ab erfasse, muss die damals gültige Adresse
    /// gelten»). Quelle = Wohnort-Historie (bestätigte Einträge, GueltigAb ≤
    /// Stichtag; der Eintrag ohne Datum = «seit jeher»). Ohne passenden
    /// Historie-Eintrag gilt die aktuelle Adresse.
    /// </summary>
    public sealed record WohnadresseAm(string? Street, string? ZipCode, string? City, string? CantonCode, string? Country, bool AusHistorie, DateOnly? GueltigAb, DateOnly? GueltigBis);

    private async Task<WohnadresseAm?> WohnadresseAmAsync(int employeeId, DateOnly stichtag)
    {
        var e = await _db.Employees.AsNoTracking()
            .Where(x => x.Id == employeeId)
            .Select(x => new { x.CantonCode, x.City, x.ZipCode, x.Street, x.Country })
            .FirstOrDefaultAsync();
        if (e == null) return null;
        var hist = await _db.EmployeeWohnortHistories.AsNoTracking()
            .Where(h => h.EmployeeId == employeeId && !h.DatumOffen)
            .OrderBy(h => h.GueltigAb == null ? 0 : 1).ThenBy(h => h.GueltigAb).ThenBy(h => h.Id)
            .ToListAsync();
        if (hist.Count > 0)
        {
            // Passender Eintrag = letzter mit GueltigAb ≤ Stichtag (oder «seit jeher»).
            // Liegt der Stichtag VOR dem ältesten datierten Eintrag, gilt der
            // älteste bekannte Wohnort (Walter 04.09.2026: 1.1.2025 → Sursee,
            // obwohl die Historie Sursee erst «ab 1.2.2025» kennt).
            EmployeeWohnortHistory? treffer = null; DateOnly? bis = null;
            for (int i = 0; i < hist.Count; i++)
            {
                var h = hist[i];
                if (h.GueltigAb == null || h.GueltigAb <= stichtag)
                {
                    treffer = h;
                    bis = (i + 1 < hist.Count && hist[i + 1].GueltigAb.HasValue) ? hist[i + 1].GueltigAb!.Value.AddDays(-1) : null;
                }
            }
            if (treffer == null && hist.Count > 1)
            {
                treffer = hist[0];
                bis = hist[1].GueltigAb.HasValue ? hist[1].GueltigAb!.Value.AddDays(-1) : null;
            }
            // Nur wenn der Historie-Stand NICHT die heutige Adresse ist
            // (sonst gilt die MA-Maske inkl. Land/Strasse als Quelle).
            if (treffer != null && bis != null
                && (!string.Equals(treffer.Plz ?? "", e.ZipCode ?? "", StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(treffer.Ort ?? "", e.City ?? "", StringComparison.OrdinalIgnoreCase)))
            {
                var kanton = (treffer.KantonCode ?? "").Trim();
                if (kanton.Length == 0 && (treffer.Plz ?? "").Trim().Length == 4)
                    kanton = (await _db.SwissLocations.AsNoTracking().Where(l => l.Plz4 == treffer.Plz!.Trim()).Select(l => l.Kantonskuerzel).FirstOrDefaultAsync() ?? "").Trim();
                return new WohnadresseAm(treffer.Strasse, treffer.Plz, treffer.Ort, kanton.Length > 0 ? kanton.ToUpperInvariant() : null, "CH", true, treffer.GueltigAb, bis);
            }
        }
        return new WohnadresseAm(e.Street, e.ZipCode, e.City, e.CantonCode, e.Country, false, null, null);
    }

    /// <summary>
    /// GET …/quellensteuer/wohnadresse-am?datum=YYYY-MM-DD — Adresse am
    /// Stichtag für den Kopf des QST-Modals (Walter 04.09.2026).
    /// </summary>
    [HttpGet("wohnadresse-am")]
    public async Task<IActionResult> WohnadresseAmStichtag(int employeeId, [FromQuery] string? datum)
    {
        var d = DateOnly.TryParse(datum, out var x) ? x : DateOnly.FromDateTime(DateTime.Today);
        var a = await WohnadresseAmAsync(employeeId, d);
        if (a == null) return NotFound();
        var kanton = (a.CantonCode ?? "").Trim().ToUpperInvariant();
        var (ziv, zivSeit, zivAusHist) = await new ZivilstandHistorieService(_db).AmAsync(employeeId, d);
        return Ok(new
        {
            street = a.Street, zipCode = a.ZipCode, city = a.City, cantonCode = kanton.Length > 0 ? kanton : null,
            kantonName = kanton.Length > 0 && KantonNamen.TryGetValue(kanton, out var kn) ? kn : null,
            country = a.Country, ausHistorie = a.AusHistorie,
            gueltigAb = a.GueltigAb?.ToString("yyyy-MM-dd"), gueltigBis = a.GueltigBis?.ToString("yyyy-MM-dd"),
            zivilstand = ziv, zivilstandSeit = zivSeit?.ToString("yyyy-MM-dd"), zivilstandAusHistorie = zivAusHist,
            stichtag = d.ToString("yyyy-MM-dd"),
        });
    }

    private async Task ApplyWohnadresseAsync(EmployeeQuellensteuer entry, int employeeId)
    {
        // Adresse AM STICHTAG (Gültig-ab der Version) — aus der Wohnort-
        // Historie, sonst aktuelle MA-Adresse (Walter 04.09.2026).
        var e = await WohnadresseAmAsync(employeeId, entry.ValidFrom);
        if (e == null) return;

        // ── Auslands-Hauptwohnsitz (Walter 28.08.2026): Land ≠ CH ⇒ Person
        // ohne steuerrechtlichen Wohnsitz CH (Grenzgänger / internationaler
        // Wochenaufenthalter). Der QST-KANTON ist dann der ARBEITSKANTON =
        // Kanton der Filiale des ältesten laufenden Vertrags (Hauptfiliale),
        // abgeleitet aus der Filial-PLZ. Zusätzlich werden die Auslands-
        // Felder der Erfassung vorbefüllt (nur wenn leer — Client darf
        // präzisieren). Tarif-Sonderwege (DE L/M/N/P/Q, FR SFN, IT R/S/T/U/V,
        // FL 0) folgen in K4 — Basis siehe docs/qst-korrektur-konzept.md. ──
        var land = (e.Country ?? "").Trim();
        var istAusland = land.Length > 0
            && !land.Equals("CH", StringComparison.OrdinalIgnoreCase)
            && !land.Equals("Schweiz", StringComparison.OrdinalIgnoreCase);
        // Grenzgänger-Flag server-autoritativ aus der Adresse (Walter
        // 29.08.2026): Land ≠ CH ⇒ automatisch Grenzgänger, CH ⇒ keiner —
        // kein manueller Weg mehr (die Checkbox ist nur noch Datenträger).
        entry.IsGrenzgaenger = istAusland;
        if (istAusland)
        {
            if (string.IsNullOrWhiteSpace(entry.Wohnsitzstaat)) entry.Wohnsitzstaat = land;
            if (string.IsNullOrWhiteSpace(entry.WohnsitzAusland)) entry.WohnsitzAusland = land;
            if (string.IsNullOrWhiteSpace(entry.AdresseAusland))
                entry.AdresseAusland = string.Join(", ",
                    new[] { e.Street, $"{e.ZipCode} {e.City}".Trim(), land }
                        .Where(s => !string.IsNullOrWhiteSpace(s)));

            // ── Kanton-Fall C (K4.6, Schulung Kanton-Fälle): Auslands-
            // Hauptwohnsitz + SCHWEIZER Wochenaufenthalts-Zusatzadresse →
            // QST-Kanton = WOCHENAUFENTHALTSKANTON (Priorität VOR dem
            // Filialkanton = Fall B). Quelle ist ausschliesslich die
            // Zusatzadresse Typ «Wochenaufenthalt» (kein manuelles Kreuz).
            var waAdr = await _db.EmployeeAddresses.AsNoTracking()
                .Where(a => a.EmployeeId == employeeId && a.AddressType == "Wochenaufenthalt")
                .Select(a => new { a.ZipCode, a.City })
                .FirstOrDefaultAsync();
            var waPlz = (waAdr?.ZipCode ?? "").Trim();
            if (waPlz.Length == 4 && waPlz.All(char.IsDigit))
            {
                var waLocs = await _db.SwissLocations.AsNoTracking()
                    .Where(l => l.Plz4 == waPlz).ToListAsync();
                var waMatch = waLocs.FirstOrDefault(l => l.Gemeindename == waAdr!.City)
                           ?? waLocs.FirstOrDefault(l => l.Ortschaftsname == waAdr!.City)
                           ?? waLocs.FirstOrDefault();
                if (waMatch != null)
                {
                    entry.Steuerkanton     = (waMatch.Kantonskuerzel ?? "").ToUpperInvariant();
                    entry.SteuerkantonName = KantonNamen.TryGetValue(entry.Steuerkanton, out var wkn) ? wkn : null;
                    entry.QstGemeinde      = waMatch.Gemeindename;
                    entry.QstGemeindeBfsNr = waMatch.BfsNr;
                    entry.IsWochenaufenthalter = true;
                    return;
                }
            }

            var jetzt = DateTime.Now;
            var filiale = await (from em in _db.Employments
                                 join c in _db.CompanyProfiles on em.CompanyProfileId equals c.Id
                                 where em.EmployeeId == employeeId
                                       && em.IsActive
                                       && em.ContractStartDate <= jetzt
                                       && (em.ContractEndDate == null || em.ContractEndDate >= jetzt)
                                 orderby em.ContractStartDate
                                 select new { c.ZipCode, c.City })
                .FirstOrDefaultAsync();

            entry.Steuerkanton = null; entry.SteuerkantonName = null;
            entry.QstGemeinde = null; entry.QstGemeindeBfsNr = null;
            var fPlz = (filiale?.ZipCode ?? "").Trim();
            if (fPlz.Length == 4)
            {
                var fLocs = await _db.SwissLocations.AsNoTracking()
                    .Where(l => l.Plz4 == fPlz).ToListAsync();
                var fMatch = fLocs.FirstOrDefault(l => l.Gemeindename == filiale!.City)
                          ?? fLocs.FirstOrDefault(l => l.Ortschaftsname == filiale!.City)
                          ?? fLocs.FirstOrDefault();
                if (fMatch != null)
                {
                    entry.Steuerkanton     = (fMatch.Kantonskuerzel ?? "").ToUpperInvariant();
                    entry.SteuerkantonName = KantonNamen.TryGetValue(entry.Steuerkanton, out var fkn) ? fkn : null;
                    entry.QstGemeinde      = fMatch.Gemeindename;
                    entry.QstGemeindeBfsNr = fMatch.BfsNr;
                }
            }
            return;
        }

        var kanton = (e.CantonCode ?? "").Trim().ToUpperInvariant();
        entry.Steuerkanton     = kanton.Length > 0 ? kanton : null;
        entry.SteuerkantonName = kanton.Length > 0 && KantonNamen.TryGetValue(kanton, out var kn) ? kn : null;
        entry.QstGemeinde      = string.IsNullOrWhiteSpace(e.City) ? null : e.City.Trim();
        entry.QstGemeindeBfsNr = null;

        var plz = (e.ZipCode ?? "").Trim();
        if (plz.Length == 4)
        {
            var locs = await _db.SwissLocations.AsNoTracking()
                .Where(l => l.Plz4 == plz).ToListAsync();
            var match = locs.FirstOrDefault(l => l.Gemeindename == entry.QstGemeinde)
                     ?? locs.FirstOrDefault(l => l.Ortschaftsname == entry.QstGemeinde)
                     ?? locs.FirstOrDefault();
            if (match != null)
            {
                entry.QstGemeindeBfsNr = match.BfsNr;
                if (string.IsNullOrWhiteSpace(entry.QstGemeinde))
                    entry.QstGemeinde = match.Gemeindename;
                if (string.IsNullOrWhiteSpace(entry.Steuerkanton) && !string.IsNullOrWhiteSpace(match.Kantonskuerzel))
                {
                    entry.Steuerkanton     = match.Kantonskuerzel.ToUpperInvariant();
                    entry.SteuerkantonName = KantonNamen.TryGetValue(entry.Steuerkanton, out var kn2) ? kn2 : null;
                }
            }
        }
    }

    /// <summary>
    /// Kirchensteuer IMMER aus der Konfession des MA (Walter-Vorgabe
    /// 12.08.2026): das Modal zeigt sie nur an, der Client-Wert wird in
    /// Create UND Update ignoriert. Gleiche Ableitung wie der Tarif-Vorschlag
    /// (QstTarifVorschlagLogic.IstKirchensteuerPflichtig). Der Y/N-Suffix
    /// im QST-Code (z.B. C3N/C3Y) wird passend nachgezogen.
    /// </summary>
    private async Task ApplyKirchensteuerAsync(EmployeeQuellensteuer entry, int employeeId)
    {
        var religion = await _db.Employees.AsNoTracking()
            .Where(x => x.Id == employeeId)
            .Select(x => x.Religion)
            .FirstOrDefaultAsync();

        entry.Kirchensteuer = QstTarifVorschlagLogic.IstKirchensteuerPflichtig(religion);

        // K4.4 (Bauplan Punkt 4, Walter 29.08.2026): der KANTON hat das
        // letzte Wort — Sperrliste GE/NE/VD/VS/TI und Kantone, deren
        // ESTV-Tarifdatei keine Y-Tarife enthält, bekommen IMMER N
        // (Ersatztarif entsprechend A0N/C0N). ApplyWohnadresseAsync läuft
        // VOR dieser Methode, entry.Steuerkanton ist also gesetzt.
        if (entry.Kirchensteuer
            && !QstTarifVorschlagLogic.KirchensteuerImKantonMoeglich(
                    entry.Steuerkanton, _tarife.HatYTarife(entry.Steuerkanton ?? "")))
        {
            entry.Kirchensteuer = false;
        }

        // Walter-Vorgabe 23.08.2026 (Fall Hristijan: tarif_code=A, aber
        // qst_code=C0N in der DB → Liste/Lohnzettel zeigten C0N, gerechnet
        // wurde mit A!): qst_code ist ein reiner ANZEIGE-Cache und wird
        // server-seitig IMMER aus Tarif + Kinderziffer + Kirchensteuer
        // abgeleitet — nie mehr aus dem Client übernommen. Nur wenn kein
        // TarifCode existiert (%-Sonderfälle), bleibt der bisherige Wert
        // mit nachgezogenem Y/N-Suffix stehen.
        if (!string.IsNullOrWhiteSpace(entry.TarifCode))
        {
            entry.QstCode = $"{entry.TarifCode.Trim().ToUpperInvariant()}"
                          + $"{entry.AnzahlKinder}{(entry.Kirchensteuer ? "Y" : "N")}";
        }
        else
        {
            var code = entry.QstCode?.Trim().ToUpperInvariant();
            if (!string.IsNullOrEmpty(code) && (code.EndsWith("Y") || code.EndsWith("N")))
                entry.QstCode = code[..^1] + (entry.Kirchensteuer ? "Y" : "N");
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // K4.1 Herleitungs-Snapshot + History-DIFF (Bauplan 2.3 Punkt 1/2,
    // Walter 29.08.2026): der Server friert pro Version die KOMPLETTE
    // Herleitungsbasis als JSON ein (nie vom Client). Der DIFF zur
    // Vorversion speist die History-Anzeige UND den Auto-Änderungsgrund.
    // ────────────────────────────────────────────────────────────────────────
    private async Task<string> BuildHerleitungSnapshotAsync(EmployeeQuellensteuer entry, int employeeId)
    {
        var e = await _db.Employees.AsNoTracking()
            .Where(x => x.Id == employeeId)
            .Select(x => new { x.MaritalStatus, x.MaritalStatusSince, x.Religion,
                               x.Country, x.CantonCode, x.NationalityId })
            .FirstOrDefaultAsync();
        var familie = await _db.EmployeeFamilyMembers.AsNoTracking()
            .Where(f => f.EmployeeId == employeeId)
            .ToListAsync();
        var natCodes = await _db.Nationalities.AsNoTracking()
            .ToDictionaryAsync(n => n.Id, n => n.Code);
        string? NatCode(int? id) => id.HasValue && natCodes.TryGetValue(id.Value, out var c) ? c : null;

        var partner  = familie.FirstOrDefault(f => f.MemberType == "Ehepartner");
        var kPartner = familie.FirstOrDefault(f => f.MemberType == "Konkubinatspartner");
        var kinder   = familie.Where(f => f.MemberType == "Kind")
            .OrderBy(f => f.DateOfBirth).ThenBy(f => f.Id).ToList();

        // Zivilstand AM STICHTAG (Walter 04.09.2026): aus der Zivilstand-
        // Historie, sonst aktueller Stand — ein Alt-Eintrag «damals
        // verheiratet» bekommt so die richtige Herleitung.
        var (zivAm, zivSeitAm, _) = await new ZivilstandHistorieService(_db).AmAsync(employeeId, entry.ValidFrom);
        var snap = new
        {
            zivilstand       = zivAm ?? e?.MaritalStatus,
            zivilstandSeit   = (zivSeitAm ?? e?.MaritalStatusSince)?.ToString("yyyy-MM-dd"),
            konfession       = e?.Religion,
            nationalitaet    = NatCode(e?.NationalityId),
            wohnLand         = string.IsNullOrWhiteSpace(e?.Country) ? "CH" : e!.Country,
            wohnKanton       = e?.CantonCode,
            steuerkanton     = entry.Steuerkanton,
            gemeindeBfs      = entry.QstGemeindeBfsNr,
            wochenaufenthalt = entry.IsWochenaufenthalter,
            grenzgaenger     = entry.IsGrenzgaenger,
            partner = partner == null ? null : new
            {
                nationalitaet   = NatCode(partner.NationalityId),
                erwerbstaetig   = partner.Erwerbstaetig,
                arbeitgeber     = partner.ArbeitgeberName,
                bewilligungBis  = partner.PermitExpiryDate?.ToString("yyyy-MM-dd"),
                lebtImHaushalt  = partner.LebtImHaushalt
            },
            konkubinat = kPartner == null ? null : new
            {
                erwerbstaetig          = kPartner.Erwerbstaetig,
                maHatHoeheresEinkommen = kPartner.MaHatHoeheresEinkommen
            },
            kinder = kinder.Select(k => new
            {
                geburtsdatum    = k.DateOfBirth?.ToString("yyyy-MM-dd"),
                lebtImHaushalt  = k.LebtImHaushalt,
                inErstausbildung = k.InErstausbildung,
                gemeinsamesKind = k.GemeinsamesKindMitPartner,
                qstBerechtigBis = k.QstDeductibleUntil?.ToString("yyyy-MM-dd")
            }).ToList(),
            resultat = new
            {
                tarifCode        = entry.TarifCode,
                qstCode          = entry.QstCode,
                kinderziffer     = entry.AnzahlKinder,
                kirchensteuer    = entry.Kirchensteuer,
                kategorie        = entry.Kategorie,
                prozentsatz      = entry.Prozentsatz,
                spezielBewilligt = entry.SpezielBewilligt
            }
        };
        return System.Text.Json.JsonSerializer.Serialize(snap);
    }

    /// <summary>Feld-Diff zweier Herleitungs-Snapshots («pfad: alt → neu»).
    /// Leere Liste, wenn einer fehlt (Alt-Versionen ohne Snapshot) oder
    /// nichts abweicht.</summary>
    private static List<string> HerleitungDiff(string? altJson, string? neuJson)
    {
        var diffs = new List<string>();
        if (string.IsNullOrWhiteSpace(altJson) || string.IsNullOrWhiteSpace(neuJson)) return diffs;
        try
        {
            var alt = new Dictionary<string, string?>();
            var neu = new Dictionary<string, string?>();
            using (var da = System.Text.Json.JsonDocument.Parse(altJson)) FlattenJson(da.RootElement, "", alt);
            using (var dn = System.Text.Json.JsonDocument.Parse(neuJson)) FlattenJson(dn.RootElement, "", neu);
            foreach (var key in alt.Keys.Union(neu.Keys).OrderBy(k => k, StringComparer.Ordinal))
            {
                alt.TryGetValue(key, out var a);
                neu.TryGetValue(key, out var n);
                if (a != n) diffs.Add($"{key}: {ChDatum(a) ?? "—"} → {ChDatum(n) ?? "—"}");
            }
        }
        catch { /* defekter Alt-Snapshot → kein Diff */ }
        return diffs;
    }

    /// <summary>ISO-Datum (yyyy-MM-dd) → CH-Anzeige dd.MM.yyyy — Daten NIE im US-/ISO-Format zeigen (Walter 04.09.2026).</summary>
    private static string? ChDatum(string? v)
        => v != null && v.Length == 10 && DateOnly.TryParseExact(v, "yyyy-MM-dd", out var d) ? d.ToString("dd.MM.yyyy") : v;

    private static void FlattenJson(System.Text.Json.JsonElement el, string prefix,
        Dictionary<string, string?> into)
    {
        switch (el.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object:
                foreach (var p in el.EnumerateObject())
                    FlattenJson(p.Value, prefix.Length == 0 ? p.Name : $"{prefix}.{p.Name}", into);
                break;
            case System.Text.Json.JsonValueKind.Array:
                int i = 0;
                foreach (var item in el.EnumerateArray())
                    FlattenJson(item, $"{prefix}[{i++}]", into);
                break;
            case System.Text.Json.JsonValueKind.Null:
            case System.Text.Json.JsonValueKind.Undefined:
                into[prefix] = null;
                break;
            default:
                into[prefix] = el.ToString();
                break;
        }
    }

    private static readonly Dictionary<string, string> KantonNamen = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AG"] = "Aargau",       ["AI"] = "Appenzell Innerrhoden", ["AR"] = "Appenzell Ausserrhoden",
        ["BE"] = "Bern",         ["BL"] = "Basel-Landschaft",      ["BS"] = "Basel-Stadt",
        ["FR"] = "Freiburg",     ["GE"] = "Genf",                  ["GL"] = "Glarus",
        ["GR"] = "Graubünden",   ["JU"] = "Jura",                  ["LU"] = "Luzern",
        ["NE"] = "Neuenburg",    ["NW"] = "Nidwalden",             ["OW"] = "Obwalden",
        ["SG"] = "St. Gallen",   ["SH"] = "Schaffhausen",          ["SO"] = "Solothurn",
        ["SZ"] = "Schwyz",       ["TG"] = "Thurgau",               ["TI"] = "Tessin",
        ["UR"] = "Uri",          ["VD"] = "Waadt",                 ["VS"] = "Wallis",
        ["ZG"] = "Zug",          ["ZH"] = "Zürich"
    };

    // ────────────────────────────────────────────────────────────────────────
    // Umzug / Kantonswechsel (Walter-Vorgabe 04.08.2026)
    //
    // AMTLICHE MONATSREGEL (ESTV Kreisschreiben 45 — NICHT «vereinfachen»!):
    // Bei einem Wohnsitzwechsel in einen anderen Kanton wird der GESAMTE
    // Umzugsmonat noch mit dem BISHERIGEN Wohnkanton abgerechnet; der neue
    // Kanton ist erst ab dem 1. des FOLGEMONATS tarif-zuständig.
    // Beispiel: Umzug 15.07. AG→LU → Juli komplett AG-Tarif, ab 01.08.
    // LU-Tarif. Meldedaten an die Kantone: beim ALTEN Kanton gilt der
    // LETZTE Tag des Umzugsmonats als Austritt, beim NEUEN der 1. des
    // Folgemonats als Eintritt.
    // ────────────────────────────────────────────────────────────────────────
    // POST /api/employee-quellensteuer/{employeeId}/umzug
    /// <summary>
    /// K1: Korrektur-Posten des MA (rückwirkende QST-Änderungen über
    /// abgeschlossene Monate) — Anzeige im QST-Tab.
    /// </summary>
    [HttpGet("korrekturen")]
    public async Task<IActionResult> GetKorrekturen(int employeeId)
    {
        var liste = await _db.QstKorrekturen
            .Where(k => k.EmployeeId == employeeId)
            .OrderByDescending(k => k.Jahr).ThenByDescending(k => k.Monat)
            .Select(k => new
            {
                k.Id, k.Jahr, k.Monat, k.NeueVersionId,
                k.AlterCode, k.NeuerCode,
                k.AlterBetrag, k.NeuerBetrag, k.Differenz,
                k.Status, k.Grund, k.CreatedAt, k.CreatedBy
            })
            .ToListAsync();
        return Ok(liste);
    }

    [HttpPost("/api/employee-quellensteuer/{employeeId:int}/umzug")]
    public async Task<IActionResult> Umzug(int employeeId, [FromBody] QstUmzugDto dto)
    {
        if (dto is null || dto.UmzugsDatum == default)
            return BadRequest(new { error = "UMZUGSDATUM_FEHLT", message = "Bitte ein Umzugsdatum angeben." });

        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId);
        if (emp is null)
            return NotFound(new { error = "MA_NICHT_GEFUNDEN", message = "Mitarbeiter nicht gefunden." });

        // Neuer Kanton: aus dem Body, sonst der aktuelle Wohnkanton des MA.
        var neuerKanton = (dto.NeuerKanton ?? emp.CantonCode ?? "").Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(neuerKanton))
            return BadRequest(new
            {
                error   = "NEUER_KANTON_FEHLT",
                message = "Neuer Kanton fehlt — weder im Request angegeben noch als Wohnkanton am Mitarbeiter erfasst."
            });

        // 1) Aktive QST-Version am Umzugsdatum (jüngstes ValidFrom, Tie-Break Id
        //    — gleiche Auswahl wie Engine/Dashboard).
        var aktiv = await _db.EmployeeQuellensteuer
            .Where(q => q.EmployeeId == employeeId
                     && q.ValidFrom <= dto.UmzugsDatum
                     && (q.ValidTo == null || q.ValidTo >= dto.UmzugsDatum))
            .OrderByDescending(q => q.ValidFrom)
            .ThenByDescending(q => q.Id)
            .FirstOrDefaultAsync();
        if (aktiv is null)
            return NotFound(new
            {
                error   = "KEINE_AKTIVE_QST_VERSION",
                message = $"Am {dto.UmzugsDatum:dd.MM.yyyy} ist keine QST-Version aktiv — bitte zuerst einen QST-Eintrag erfassen."
            });

        // 2) QST-History NUR bei Wechsel von ORT und/oder KANTON (Walter
        //    12.08.2026): ändert sich nur Strasse/Hausnummer, bleibt die
        //    QST-Zuständigkeit identisch — KEIN neuer History-Eintrag.
        var alterKanton  = (aktiv.Steuerkanton ?? "").Trim().ToUpperInvariant();
        var alteGemeinde = (aktiv.QstGemeinde ?? "").Trim();
        var neueGemeindeMa = (emp.City ?? "").Trim();
        bool kantonsWechsel = !string.Equals(alterKanton, neuerKanton, StringComparison.OrdinalIgnoreCase);
        bool ortsWechsel    = neueGemeindeMa.Length > 0
                              && !string.Equals(alteGemeinde, neueGemeindeMa, StringComparison.OrdinalIgnoreCase);
        if (!kantonsWechsel && !ortsWechsel)
            return BadRequest(new
            {
                error   = "KEIN_WECHSEL",
                message = $"Weder Ort noch Kanton haben geändert (weiterhin {neueGemeindeMa} {neuerKanton}) — " +
                          "ein Strassen-/Hausnummer-Wechsel braucht keinen neuen QST-Eintrag."
            });

        // 3) Wechselstichtag = 1. Tag des Monats NACH dem Umzugsdatum
        //    (Monatsregel Kreisschreiben 45). SPEZIALFALL Umzug am 1. des
        //    Monats (Walter 08.08.2026): dann ist im alten Kanton NICHTS
        //    angebrochen — der neue Kanton gilt ab genau diesem Tag.
        var stichtag = dto.UmzugsDatum.Day == 1
            ? dto.UmzugsDatum
            : new DateOnly(dto.UmzugsDatum.Year, dto.UmzugsDatum.Month, 1).AddMonths(1);
        var letzterTagUmzugsmonat = stichtag.AddDays(-1);

        // 7) Lohnlauf-Edit-Lock — gleiches Soft-Lock-Muster wie Create/Update:
        //    QST bleibt bis Definitiv-Abschluss editierbar; liegt der Stichtag
        //    in einer definitiv abgeschlossenen Periode → 409.
        var branchId     = await GetEmployeeBranchAsync(employeeId);
        var firstAllowed = await GetQstFirstAllowedAsync(branchId);
        if (firstAllowed.HasValue && stichtag < firstAllowed.Value)
        {
            return Conflict(new
            {
                error            = "LOHN_EDIT_LOCKED",
                message          = $"Der Kantonswechsel-Stichtag {stichtag:dd.MM.yyyy} liegt in einer definitiv abgeschlossenen Lohnperiode. " +
                                   $"Frühestes erlaubtes «Gültig ab»: {firstAllowed.Value:dd.MM.yyyy}.",
                firstAllowedDate = firstAllowed.Value.ToString("yyyy-MM-dd")
            });
        }

        // Schutz vor Überlappung: existiert bereits eine SPÄTERE Version
        // (ValidFrom nach dem Umzugsdatum), würde das Kappen/Anlegen hier
        // die Versionskette zerreissen → manuell im QST-Tab pflegen.
        var spaetere = await _db.EmployeeQuellensteuer
            .Where(q => q.EmployeeId == employeeId && q.Id != aktiv.Id && q.ValidFrom > dto.UmzugsDatum)
            .OrderBy(q => q.ValidFrom)
            .FirstOrDefaultAsync();
        if (spaetere != null)
        {
            return Conflict(new
            {
                error   = "QST_FOLGEVERSION_VORHANDEN",
                message = $"Es existiert bereits eine spätere QST-Version ab {spaetere.ValidFrom:dd.MM.yyyy}. " +
                          "Der Umzug kann nicht automatisch erfasst werden — bitte die Versionen im QST-Tab manuell anpassen."
            });
        }

        var now = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified);

        // 4) Alte Version auf den letzten Tag des Umzugsmonats begrenzen
        //    (nur wenn ValidTo offen oder später).
        var altesValidTo = aktiv.ValidTo;
        if (aktiv.ValidTo == null || aktiv.ValidTo > letzterTagUmzugsmonat)
        {
            aktiv.ValidTo   = letzterTagUmzugsmonat;
            aktiv.UpdatedAt = now;
        }

        // Neue Gemeinde/BFS nur wenn der neue Kanton dem Wohnkanton des MA
        // entspricht (dann kennen wir die Adresse) — Lookup über die PLZ.
        // Sonst leer lassen; die alte Gemeinde wäre im neuen Kanton falsch.
        string? neueGemeinde = null;
        int?    neueBfsNr    = null;
        if (!string.IsNullOrWhiteSpace(emp.ZipCode)
            && string.Equals(neuerKanton, (emp.CantonCode ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
        {
            var plz = emp.ZipCode.Trim();
            var loc = await _db.SwissLocations
                .Where(l => l.Plz4 == plz && l.Kantonskuerzel == neuerKanton)
                .OrderBy(l => l.Id)
                .FirstOrDefaultAsync();
            if (loc != null)
            {
                neueGemeinde = loc.Gemeindename;
                neueBfsNr    = loc.BfsNr;
            }
        }

        // 5) Neue Version = Kopie der alten (Tarif, Kirchensteuer, Kinder, alle
        //    Felder), nur Steuerkanton/Gemeinde neu. ValidTo übernimmt ein
        //    altes Ende, das NACH dem Stichtag lag (z.B. befristete Erfassung).
        var neu = new EmployeeQuellensteuer
        {
            EmployeeId                   = employeeId,
            ValidFrom                    = stichtag,
            ValidTo                      = (altesValidTo.HasValue && altesValidTo.Value >= stichtag) ? altesValidTo : null,
            Steuerkanton                 = neuerKanton,
            SteuerkantonName             = KantonNamen.TryGetValue(neuerKanton, out var kn) ? kn : null,
            QstGemeinde                  = neueGemeinde,
            QstGemeindeBfsNr             = neueBfsNr,
            TarifvorschlagQst            = aktiv.TarifvorschlagQst,
            TarifCode                    = aktiv.TarifCode,
            TarifBezeichnung             = aktiv.TarifBezeichnung,
            AnzahlKinder                 = aktiv.AnzahlKinder,
            Kirchensteuer                = aktiv.Kirchensteuer,
            QstCode                      = aktiv.QstCode,
            SpezielBewilligt             = aktiv.SpezielBewilligt,
            Kategorie                    = aktiv.Kategorie,
            Prozentsatz                  = aktiv.Prozentsatz,
            MindestlohnSatzbestimmung    = aktiv.MindestlohnSatzbestimmung,
            PartnerEmployeeId            = aktiv.PartnerEmployeeId,
            PartnerEinkommenVon          = aktiv.PartnerEinkommenVon,
            PartnerEinkommenBis          = aktiv.PartnerEinkommenBis,
            ArbeitsortKanton             = aktiv.ArbeitsortKanton,
            WeitereBeschaftigungen       = aktiv.WeitereBeschaftigungen,
            GesamtpensumWeitereAg        = aktiv.GesamtpensumWeitereAg,
            GesamteinkommenWeitereAg     = aktiv.GesamteinkommenWeitereAg,
            Halbfamilie                  = aktiv.Halbfamilie,
            WohnsitzAusland              = aktiv.WohnsitzAusland,
            Wohnsitzstaat                = aktiv.Wohnsitzstaat,
            AdresseAusland               = aktiv.AdresseAusland,
            LivesInKonkubinat            = aktiv.LivesInKonkubinat,
            HasJointParentalCare         = aktiv.HasJointParentalCare,
            PaysAlimonyAdultChildren     = aktiv.PaysAlimonyAdultChildren,
            HasHigherIncomeThanPartner   = aktiv.HasHigherIncomeThanPartner,
            IsGrenzgaenger               = aktiv.IsGrenzgaenger,
            IsWochenaufenthalter         = aktiv.IsWochenaufenthalter,
            CreatedAt                    = now,
            UpdatedAt                    = now
        };
        _db.EmployeeQuellensteuer.Add(neu);
        await _db.SaveChangesAsync();

        // 6) Audit-Log — Actor IMMER aus dem JWT (Walter-Vorgabe 20.05.2026),
        //    das Model hat kein Bemerkungsfeld.
        var actor = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "?";
        _log.LogInformation(
            "QST-Umzug MA {EmployeeId}: {Alt}→{Neu} per {Umzug}, Kantonswechsel ab {Stichtag} (Umzugsmonat noch {AltMonat}) — Actor UserId {Actor}",
            employeeId, alterKanton, neuerKanton,
            dto.UmzugsDatum.ToString("dd.MM.yyyy"), stichtag.ToString("dd.MM.yyyy"), alterKanton, actor);

        // 8) Beide Versionen + die zwei Meldedaten für die Quellensteuermeldung
        //    an die Kantone (alt = Austritt, neu = Eintritt).
        return Ok(new
        {
            alteVersion         = MapToDto(aktiv, firstAllowed),
            neueVersion         = MapToDto(neu, firstAllowed),
            alterKanton,
            neuerKanton,
            alterKantonAustritt = letzterTagUmzugsmonat.ToString("yyyy-MM-dd"),
            neuerKantonEintritt = stichtag.ToString("yyyy-MM-dd"),
            message             = $"Umzug {alterKanton}→{neuerKanton} per {dto.UmzugsDatum:dd.MM.yyyy} erfasst. " +
                                  $"Umzugsmonat noch {alterKanton}, {neuerKanton} gilt ab {stichtag:dd.MM.yyyy}. " +
                                  $"Meldung: {alterKanton} Austritt {letzterTagUmzugsmonat:dd.MM.yyyy}, {neuerKanton} Eintritt {stichtag:dd.MM.yyyy}."
        });
    }
}

/// <summary>Request-DTO für den QST-Umzug (Kantonswechsel).</summary>
public class QstUmzugDto
{
    public DateOnly UmzugsDatum { get; set; }
    /// <summary>Optional — Default = Wohnkanton des MA (employee.canton_code).</summary>
    public string? NeuerKanton { get; set; }
}
