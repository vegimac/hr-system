using System.Security.Claims;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Gruppen-E-Mail an Mitarbeitende (Walter-Vorgabe 14.08.2026) — erste
/// geöffnete Funktion der «Mitarbeiter-Korrespondenz». Selektion:
/// Filiale (eine oder alle) × Vertragsmodell (FLEX/MTP/FIX/FIX-M, mehrere
/// wählbar) → Empfänger-Vorschau mit Abwahl → Versand als EINZELMAILS an
/// employee.email (kein CC/BCC — Adressen bleiben privat). Versand über
/// den bestehenden EmailService (SMTP-Konfig, Test-Redirect greift).
/// Anwendungsfall #1: Dienstplan-Handy-Link ans Management-Team (FIX-M).
/// Verteiler-Kategorie GRUPPEN_MAIL — die Freigabe steuert Walter über
/// die Haken-Matrix in der Systemsteuerung.
/// Der Versand von Lohnbelegen bleibt bewusst GESCHLOSSEN.
/// </summary>
[Authorize(Roles = "admin,superuser")]
[ApiController]
[Route("api/ma-email")]
public class MaEmailController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly EmailService _email;
    private readonly VersandFreigabeService _freigabe;

    public MaEmailController(AppDbContext db, EmailService email, VersandFreigabeService freigabe)
    {
        _db = db;
        _email = email;
        _freigabe = freigabe;
    }

    // ── GET /api/ma-email/empfaenger?companyProfileId=&modelle=FIX-M,MTP ─
    /// <summary>
    /// Empfänger-Vorschau: aktive MA mit heute laufendem Vertrag der
    /// gewählten Modelle in der gewählten Filiale (leer = alle Filialen).
    /// MA ohne E-Mail-Adresse werden mitgeliefert (Anzeige «keine E-Mail»),
    /// sind aber nicht versandfähig.
    /// </summary>
    [HttpGet("empfaenger")]
    /// <param name="nurBenutzer">
    /// true = NUR die OneCrew-Benutzer, keine Mitarbeitenden (Walter-Vorgabe
    /// 01.09.2026). Ohne diesen Schalter liesse sich der Fall gar nicht
    /// ausdrücken: «kein Vertragsmodell gewählt» heisst überall sonst
    /// «alle Modelle» — hier soll es aber «gar keine MA» heissen.
    /// </param>
    public async Task<IActionResult> Empfaenger(
        [FromQuery] int? companyProfileId, [FromQuery] string? modelle,
        [FromQuery] string? funktionen, [FromQuery] bool benutzer = false,
        [FromQuery] bool nurBenutzer = false)
    {
        var wanted = (modelle ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(m => m.ToUpperInvariant())
            .ToHashSet();
        // Funktions-Filter (Walter 15.08.2026): JobGroup-Codes, leer = alle.
        var wantedFunk = (funktionen ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(f => f.ToUpperInvariant())
            .ToHashSet();

        var heute = DateTime.Today;
        var rows = await _db.Employments.AsNoTracking()
            .Where(em => !nurBenutzer
                      && em.IsActive
                      && em.ContractStartDate <= heute
                      && (em.ContractEndDate == null || em.ContractEndDate >= heute)
                      && em.Employee!.IsActive
                      && !em.Employee!.IsHidden
                      && !em.Employee!.IsPayrollExcluded
                      && (companyProfileId == null || em.CompanyProfileId == companyProfileId))
            .Select(em => new
            {
                em.EmployeeId, em.CompanyProfileId, em.ContractStartDate, em.EmploymentModel,
                em.JobGroupId, em.JobTitle,
                em.Employee!.FirstName, em.Employee!.LastName, em.Employee!.EmployeeNumber,
                em.Employee!.Email,
            })
            .ToListAsync();

        // JobGroup-Code + deutscher Anzeigename (app_text JOB_GROUP).
        var jobGroups = await _db.JobGroups.AsNoTracking()
            .Select(j => new { j.Id, j.Code })
            .ToDictionaryAsync(j => j.Id, j => j.Code);
        var jgNames = await _db.AppTexts.AsNoTracking()
            .Where(t => t.IsActive && t.Module == "JOB_GROUP" && t.LanguageCode == "de")
            .ToDictionaryAsync(t => t.TextKey, t => t.Content);

        // Pro MA der jüngste laufende Vertrag bestimmt Modell + Filiale.
        string? FunkCode(int? jobGroupId, string? jobTitle) =>
            jobGroupId.HasValue && jobGroups.TryGetValue(jobGroupId.Value, out var jc) ? jc : jobTitle;

        var proMa = rows
            .GroupBy(x => x.EmployeeId)
            .Select(g => g.OrderByDescending(x => x.ContractStartDate).First())
            .Where(x => wanted.Count == 0 || wanted.Contains((x.EmploymentModel ?? "").ToUpperInvariant()))
            .Where(x => wantedFunk.Count == 0
                     || wantedFunk.Contains((FunkCode(x.JobGroupId, x.JobTitle) ?? "").ToUpperInvariant()))
            .ToList();

        var branches = await _db.CompanyProfiles.AsNoTracking()
            .Select(c => new { c.Id, c.City, c.BranchName, c.WorkLocation })
            .ToDictionaryAsync(c => c.Id);

        var result = proMa
            .OrderBy(x => x.FirstName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.LastName, StringComparer.OrdinalIgnoreCase)
            .Select(x => new EmpfaengerZeile(
                Art:            "MA",
                EmployeeId:     x.EmployeeId,
                UserId:         null,
                Name:           $"{x.FirstName} {x.LastName}".Trim(),
                EmployeeNumber: x.EmployeeNumber,
                Modell:         x.EmploymentModel,
                Filiale:        x.CompanyProfileId.HasValue && branches.TryGetValue(x.CompanyProfileId.Value, out var b)
                                    ? (!string.IsNullOrWhiteSpace(b.WorkLocation) ? b.WorkLocation : (b.City ?? b.BranchName))
                                    : null,
                Email:          string.IsNullOrWhiteSpace(x.Email) ? null : x.Email.Trim(),
                Funktion:       FunkCode(x.JobGroupId, x.JobTitle) is string fc && fc.Length > 0
                                    ? (jgNames.TryGetValue(fc + ".NAME", out var fn) ? fn : fc)
                                    : null))
            .ToList();

        // ── OneCrew-Benutzer dazunehmen (Walter-Vorgabe 01.09.2026) ─────────
        // Geschäftsführer sind BEIDES: Mitarbeitende und OneCrew-Benutzer. Wer
        // an beide Gruppen schreibt, würde sie zweimal anschreiben.
        //
        // WICHTIG (Walter-Korrektur 01.09.2026): Gemeint sind die Benutzer, die
        // OneCrew BEDIENEN — Backoffice, GF, HR, Buchhaltung. Die Rolle
        // "employee" ist dagegen der MA-Postfach-Account, den JEDER Mitarbeiter
        // automatisch bekommt (Login mp-<Personalnummer>@schaub.local). Ohne
        // diesen Filter standen 296 solcher Accounts in der Liste, alle mit
        // einer erfundenen .local-Adresse, an die gar keine Mail zustellbar ist.
        // Gleiches Muster wie MailboxController/DocumentsController: Role != "employee".
        if (benutzer)
        {
            var users = await _db.AppUsers.AsNoTracking()
                .Where(u => u.IsActive
                         && u.Role != "employee"
                         && u.Email != ""
                         && !u.Email.EndsWith(".local"))
                .Select(u => new { u.Id, u.FirstName, u.LastName, u.Username, u.Email, u.Role })
                .ToListAsync();
            result.AddRange(users
                .OrderBy(u => u.FirstName, StringComparer.OrdinalIgnoreCase)
                .Select(u => new EmpfaengerZeile(
                    Art:            "BENUTZER",
                    EmployeeId:     null,
                    UserId:         u.Id,
                    Name:           string.IsNullOrWhiteSpace(($"{u.FirstName} {u.LastName}").Trim())
                                        ? u.Username : $"{u.FirstName} {u.LastName}".Trim(),
                    EmployeeNumber: null,
                    Modell:         null,
                    Filiale:        null,
                    Email:          u.Email.Trim(),
                    Funktion:       u.Role)));
        }

        // ── Doppelte E-Mail-Adressen entfernen (Walter-Vorgabe 01.09.2026) ──
        // Gilt IMMER, nicht nur beim Benutzer-Versand: dieselbe Adresse kann
        // auch bei zwei MA-Datensätzen stehen (Doppelerfassung, Familienadresse).
        // Der ERSTE Treffer gewinnt — also der Mitarbeitende, weil MA zuerst in
        // der Liste stehen. So bleibt der Eintrag mit Filiale und Funktion, der
        // beim Prüfen der Liste mehr sagt als ein blosser Benutzername.
        var gesehen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entdoppelt = new List<EmpfaengerZeile>();
        var doppelte = 0;
        foreach (var z in result)
        {
            if (string.IsNullOrWhiteSpace(z.Email)) { entdoppelt.Add(z); continue; }
            if (!gesehen.Add(z.Email!.Trim())) { doppelte++; continue; }
            entdoppelt.Add(z);
        }

        return Ok(new { zeilen = entdoppelt, doppelteEntfernt = doppelte });
    }

    /// <summary>
    /// Eine Zeile der Empfängerliste. <c>Art</c> unterscheidet Mitarbeitende
    /// von OneCrew-Benutzern — beide landen in derselben Liste, damit die
    /// Entdoppelung über die E-Mail-Adresse greifen kann.
    /// </summary>
    public record EmpfaengerZeile(
        string Art, int? EmployeeId, int? UserId, string Name, string? EmployeeNumber,
        string? Modell, string? Filiale, string? Email, string? Funktion);

    // ── POST /api/ma-email/senden ────────────────────────────────────────
    /// <summary>
    /// Versand als Einzelmails. Text 1:1 (Zeilenumbrüche → &lt;br&gt;).
    /// Liefert pro MA das Resultat — Mail-Fehler brechen den Lauf nicht ab.
    /// </summary>
    [HttpPost("senden")]
    [RequestSizeLimit(30_000_000)]   // 30 MB — ein Anhang, viele Empfänger
    public async Task<IActionResult> Senden(
        [FromForm] string? betreff,
        [FromForm] string? text,
        [FromForm] string? employeeIds,
        [FromForm] string? userIds,
        [FromForm] IFormFile? anhang,
        // Nur fürs Protokoll (Walter 01.09.2026): die Selektion im Klartext,
        // so wie sie im Fenster stand. Aus «6 gesendet» lässt sich später
        // nicht mehr rekonstruieren, WER gemeint war.
        [FromForm] string? filialeText,
        [FromForm] string? modelleText,
        [FromForm] string? funktionenText,
        [FromForm] bool mitBenutzern = false)
    {
        if (string.IsNullOrWhiteSpace(betreff))
            return BadRequest(new { error = "BETREFF_FEHLT", message = "Bitte einen Betreff eingeben." });

        var maIds   = ParseIds(employeeIds);
        var uIds    = ParseIds(userIds);
        if (maIds.Count == 0 && uIds.Count == 0)
            return BadRequest(new { error = "KEINE_EMPFAENGER", message = "Bitte mindestens einen Empfänger wählen." });

        // Text darf fehlen, WENN ein Anhang da ist (Walter-Vorgabe 01.09.2026):
        // «nur Betreff und ein Dokument» ist ein gültiger Fall. Ganz ohne
        // beides wäre die Mail leer — das bleibt gesperrt.
        var hatAnhang = anhang != null && anhang.Length > 0;
        if (string.IsNullOrWhiteSpace(text) && !hatAnhang)
            return BadRequest(new { error = "TEXT_FEHLT",
                message = "Bitte einen Nachrichtentext eingeben oder ein Dokument anhängen." });

        List<(byte[] Data, string Name)>? anhaenge = null;
        var anhangName = "";
        if (hatAnhang)
        {
            using var ms = new MemoryStream();
            await anhang!.CopyToAsync(ms);
            anhangName = Path.GetFileName(anhang.FileName);
            anhaenge = new List<(byte[], string)> { (ms.ToArray(), anhangName) };
        }

        var reinText = (text ?? "").Trim();

        // Gleicher OneCrew-Rahmen wie bei den internen Hinweis-Mails
        // (Walter-Vorgabe 01.09.2026): Logo-Kopf im warmen Ton, weisse Karte.
        // Betreff = Titel im Kopf. white-space:pre-line uebernimmt die
        // Zeilenumbrueche aus dem Textfeld, darum kein <br>-Ersatz mehr.
        string Enc(string? v) => System.Net.WebUtility.HtmlEncode(v ?? "");

        var textHtml = string.IsNullOrEmpty(reinText)
            ? ""
            : $@"      <div style=""font-size:14px;line-height:1.6;color:#0f172a;white-space:pre-line"">{Enc(reinText)}</div>";

        // Walter-Vorgabe 01.09.2026: KEIN Hinweis auf den Anhang und KEIN
        // Fusstext. Das Dokument sieht der Empfänger ohnehin als Anhang in
        // seinem Mail-Programm — es doppelt im Text zu nennen wirkt behördlich.
        // Leerstring als Fusszeile heisst ausdrücklich «kein Fuss»
        // (null wäre der Standardtext).
        var html = EmailService.HtmlRahmen(betreff.Trim(), textHtml, "");

        // Text-Teil für Clients ohne HTML. Ohne Nachrichtentext bleibt der
        // Betreff — leer darf der Text-Teil nicht sein.
        var textBody = string.IsNullOrEmpty(reinText) ? betreff.Trim() : reinText;

        // Empfänger einsammeln: Mitarbeitende UND OneCrew-Benutzer.
        var ziele = new List<(int? EmpId, string Name, string? Email)>();

        if (maIds.Count > 0)
        {
            // Erneut gegen die AKTIV-Bedingung prüfen (Walter-Vorgabe): die
            // Liste im Browser kann alt sein — wer inzwischen ausgetreten ist,
            // darf keine Gruppen-Mail mehr bekommen.
            var heute = DateTime.Today;
            var aktiv = await _db.Employments.AsNoTracking()
                .Where(em => em.IsActive
                          && em.ContractStartDate <= heute
                          && (em.ContractEndDate == null || em.ContractEndDate >= heute)
                          && em.Employee!.IsActive && !em.Employee!.IsHidden
                          && maIds.Contains(em.EmployeeId))
                .Select(em => em.EmployeeId)
                .Distinct()
                .ToListAsync();

            var emps = await _db.Employees.AsNoTracking()
                .Where(e => aktiv.Contains(e.Id))
                .Select(e => new { e.Id, e.FirstName, e.LastName, e.Email })
                .ToListAsync();
            foreach (var e in emps)
                ziele.Add((e.Id, $"{e.FirstName} {e.LastName}".Trim(), e.Email));
        }

        if (uIds.Count > 0)
        {
            // Dieselbe Bedingung wie in der Vorschau nochmals prüfen — die
            // Liste im Browser kann alt sein, und MA-Postfach-Accounts
            // (Role "employee") dürfen nie über diesen Weg angeschrieben werden.
            var users = await _db.AppUsers.AsNoTracking()
                .Where(u => u.IsActive
                         && u.Role != "employee"
                         && u.Email != ""
                         && !u.Email.EndsWith(".local")
                         && uIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FirstName, u.LastName, u.Username, u.Email })
                .ToListAsync();
            foreach (var u in users)
            {
                var nm = $"{u.FirstName} {u.LastName}".Trim();
                ziele.Add((null, string.IsNullOrWhiteSpace(nm) ? u.Username : nm, u.Email));
            }
        }

        // ── Versandprotokoll VOR dem Senden anlegen (Walter 01.09.2026) ────
        // Die Zeile muss existieren, bevor die erste Mail rausgeht: nur so
        // kann jede einzelne mail_log-Zeile auf diesen Versand verweisen und
        // «5 fehlgeschlagen» später aufgeklappt werden. Zählerstände kommen
        // am Schluss dazu.
        // Nebeneffekt, der uns entgegenkommt: Bricht der Versand mittendrin
        // ab, steht der angefangene Lauf trotzdem im Protokoll — vorher wäre
        // er spurlos verschwunden.
        var scharf = await _freigabe.IstScharfAsync(
            VersandKategorie.GruppenMail, VersandFreigabeService.Kanal.Mail);

        GruppenMailLog? protokoll = null;
        try
        {
            protokoll = new GruppenMailLog
            {
                GesendetAm        = DateTime.Now,
                GesendetVonUserId = GetCurrentUserId(),
                Betreff           = betreff.Trim(),
                Filiale           = string.IsNullOrWhiteSpace(filialeText)    ? "Alle Filialen" : filialeText.Trim(),
                Modelle           = string.IsNullOrWhiteSpace(modelleText)    ? "alle"          : modelleText.Trim(),
                Funktionen        = string.IsNullOrWhiteSpace(funktionenText) ? "alle"          : funktionenText.Trim(),
                MitBenutzern      = mitBenutzern,
                AnhangName        = hatAnhang ? anhangName : null,
                MitText           = !string.IsNullOrEmpty(reinText),
                Scharf            = scharf,
            };
            _db.GruppenMailLogs.Add(protokoll);
            await _db.SaveChangesAsync();
        }
        catch { protokoll = null; /* Protokoll ist Beiwerk, der Versand zählt */ }

        var gesendet = new List<object>();
        var fehlgeschlagen = new List<object>();
        var ohneEmail = new List<object>();
        var uebersprungen = new List<object>();

        // Letzte Entdoppelung direkt vor dem Versand (Walter-Vorgabe
        // 01.09.2026): Geschäftsführer stehen als MA UND als Benutzer in der
        // Auswahl. Die Liste im Browser ist bereits entdoppelt — hier zählt
        // aber, was WIRKLICH rausgeht, und darauf darf man sich nicht auf den
        // Browser verlassen.
        var schonGesendet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var z in ziele)
        {
            if (string.IsNullOrWhiteSpace(z.Email))
            {
                ohneEmail.Add(new { name = z.Name });
                continue;
            }
            var adr = z.Email.Trim();
            if (!schonGesendet.Add(adr))
            {
                uebersprungen.Add(new { name = z.Name, email = adr });
                continue;
            }

            // Kategorie GRUPPEN_MAIL — ob scharf oder an die Test-Adresse,
            // entscheidet der Haken in der Systemsteuerung (Walter 01.09.2026).
            var ok = anhaenge == null
                ? await _email.SendAsync(adr, z.Name, betreff.Trim(), html, textBody,
                      VersandKategorie.GruppenMail, z.EmpId, protokoll?.Id)
                : await _email.SendWithAttachmentsAsync(adr, z.Name, betreff.Trim(), html, textBody,
                      anhaenge, VersandKategorie.GruppenMail, z.EmpId, protokoll?.Id);

            if (ok) gesendet.Add(new { name = z.Name, email = adr });
            else fehlgeschlagen.Add(new { name = z.Name, email = adr });
        }

        // ── Zählerstände nachtragen ─────────────────────────────────────────
        // Best effort: ein Protokollfehler darf einen erfolgten Versand nicht
        // als Fehler erscheinen lassen — die Mails sind ja schon draussen.
        if (protokoll != null)
        {
            try
            {
                protokoll.AnzahlGesendet       = gesendet.Count;
                protokoll.AnzahlFehlgeschlagen = fehlgeschlagen.Count;
                protokoll.AnzahlDoppelt        = uebersprungen.Count;
                protokoll.AnzahlOhneEmail      = ohneEmail.Count;
                await _db.SaveChangesAsync();
            }
            catch { /* siehe oben */ }
        }

        return Ok(new
        {
            gesendet = gesendet.Count,
            fehlgeschlagen,
            ohneEmail,
            uebersprungen,          // doppelte Adressen (GF = MA + Benutzer)
            anhang = hatAnhang ? Path.GetFileName(anhang!.FileName) : null,
            details = gesendet,
        });
    }

    // ── GET /api/ma-email/log ────────────────────────────────────────────
    /// <summary>
    /// Die letzten Gruppen-Versände. Ein Eintrag pro Versand, nicht pro
    /// Empfänger — wer wirklich was bekommen hat, steht in mail_log.
    /// </summary>
    [HttpGet("log")]
    public async Task<IActionResult> Log([FromQuery] int limit = 25)
    {
        // Namen bewusst als Einzelteile holen und ERST im Speicher
        // zusammensetzen: ein Pattern-Match (is) ist im Ausdrucksbaum von
        // EF Core nicht erlaubt.
        var roh = await _db.GruppenMailLogs.AsNoTracking()
            .OrderByDescending(l => l.GesendetAm)
            .Take(Math.Clamp(limit, 1, 200))
            .Select(l => new
            {
                l.Id,
                l.GesendetAm,
                l.Betreff,
                l.Filiale,
                l.Modelle,
                l.Funktionen,
                l.MitBenutzern,
                l.AnzahlGesendet,
                l.AnzahlFehlgeschlagen,
                l.AnzahlDoppelt,
                l.AnzahlOhneEmail,
                l.AnzahlSpaeterZugestellt,
                l.AnhangName,
                l.MitText,
                l.Scharf,
                VorName  = l.GesendetVonUser == null ? null : l.GesendetVonUser.FirstName,
                NachName = l.GesendetVonUser == null ? null : l.GesendetVonUser.LastName,
                Login    = l.GesendetVonUser == null ? null : l.GesendetVonUser.Username,
            })
            .ToListAsync();

        var rows = roh.Select(l =>
        {
            var name = $"{l.VorName} {l.NachName}".Trim();
            return new
            {
                l.Id,
                l.GesendetAm,
                l.Betreff,
                l.Filiale,
                l.Modelle,
                l.Funktionen,
                l.MitBenutzern,
                l.AnzahlGesendet,
                l.AnzahlFehlgeschlagen,
                l.AnzahlDoppelt,
                l.AnzahlOhneEmail,
                l.AnzahlSpaeterZugestellt,
                l.AnhangName,
                l.MitText,
                l.Scharf,
                von = string.IsNullOrEmpty(name) ? l.Login : name,
            };
        }).ToList();

        return Ok(rows);
    }

    // ── GET /api/ma-email/log/{id}/details ───────────────────────────────
    /// <summary>
    /// Die einzelnen Empfänger eines Gruppen-Versands, mit Grund bei
    /// Fehlschlag (Walter-Vorgabe 01.09.2026).
    ///
    /// Standardmässig nur die FEHLGESCHLAGENEN — das ist die Frage, die man
    /// sich stellt, wenn im Log «5 fehlgeschlagen» steht. Mit alle=true
    /// kommen auch die erfolgreichen dazu.
    /// </summary>
    [HttpGet("log/{id:int}/details")]
    public async Task<IActionResult> LogDetails(int id, [FromQuery] bool alle = false)
    {
        var kopf = await _db.GruppenMailLogs.AsNoTracking()
            .Where(l => l.Id == id)
            .Select(l => new { l.Id, l.GesendetAm, l.Betreff })
            .FirstOrDefaultAsync();
        if (kopf == null) return NotFound();

        var q = _db.MailLogs.AsNoTracking().Where(l => l.GruppenMailLogId == id);
        var hatVerweis = await q.AnyAsync();

        // ── Rückfall für Versände von VOR dem 01.09.2026 ────────────────────
        // Damals kannte mail_log den Versand noch nicht. Die Zeilen sind aber
        // da und lassen sich eindeutig zuordnen: Der Protokolleintrag wurde
        // früher NACH dem Versand geschrieben, also liegen alle Mails eines
        // Laufs zwischen dem Zeitstempel des VORIGEN Eintrags und dem eigenen.
        // Zwei Läufe mit gleichem Betreff kurz nacheinander (21:34 und 21:39
        // am 01.09.) lassen sich so sauber trennen — über Betreff und ein
        // pauschales Zeitfenster ginge das nicht.
        var hergeleitet = false;
        if (!hatVerweis)
        {
            var vorheriger = await _db.GruppenMailLogs.AsNoTracking()
                .Where(l => l.GesendetAm < kopf.GesendetAm)
                .OrderByDescending(l => l.GesendetAm)
                .Select(l => (DateTime?)l.GesendetAm)
                .FirstOrDefaultAsync();
            var von = vorheriger ?? kopf.GesendetAm.AddHours(-2);
            var bis = kopf.GesendetAm.AddMinutes(2);

            q = _db.MailLogs.AsNoTracking()
                .Where(l => l.GruppenMailLogId == null
                         && l.Kategorie == "GRUPPEN_MAIL"
                         && l.Subject == kopf.Betreff
                         && l.CreatedAt > von
                         && l.CreatedAt <= bis);
            hergeleitet = true;
        }

        // Wer über die Wiedervorlage später doch noch beliefert wurde
        // (Walter-Vorgabe 01.09.2026). Ohne diese Zeile stünde derselbe
        // Empfänger für immer als «fehlgeschlagen» in der Liste, obwohl die
        // Mail eine Stunde später angekommen ist — und man würde ihn
        // vergeblich von Hand nochmals anschreiben.
        // Bewusst VOR dem Filter unten abgefragt: die Erfolgs-Zeile der
        // Wiederholung fällt sonst durch das «nur fehlgeschlagene» Raster.
        var spaeterOk = await q
            .Where(l => l.Ok && l.Wiedervorlage && l.ToEmail != null)
            .Select(l => l.ToEmail!)
            .Distinct()
            .ToListAsync();
        var spaeterSet = new HashSet<string>(spaeterOk, StringComparer.OrdinalIgnoreCase);

        // Läuft für einen Empfänger noch eine Wiederholung? Das MUSS in der
        // Liste stehen: Wer «5 fehlgeschlagen» aufklappt und die fünf sofort
        // von Hand nachfasst, während OneCrew es in 15 Minuten ohnehin
        // nochmals versucht, verschickt die Mail doppelt.
        var offeneWv = await _db.MailWiedervorlagen.AsNoTracking()
            .Where(w => w.GruppenMailLogId == id && w.Status == MailWiedervorlage.StatusOffen)
            .Select(w => new { w.ToEmail, w.EffektiveAdresse, w.NaechsterVersuch })
            .ToListAsync();

        var offenBis = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        foreach (var w in offeneWv)
        {
            var adr = w.ToEmail ?? w.EffektiveAdresse;
            if (string.IsNullOrWhiteSpace(adr)) continue;
            // Bei mehreren Einträgen zur selben Adresse zählt der nächste.
            if (!offenBis.TryGetValue(adr, out var bisher) || w.NaechsterVersuch < bisher)
                offenBis[adr] = w.NaechsterVersuch;
        }

        if (!alle) q = q.Where(l => !l.Ok);

        var roh2 = await q
            .OrderBy(l => l.Ok).ThenBy(l => l.CreatedAt)
            .Take(500)
            .Select(l => new
            {
                l.Id,
                l.CreatedAt,
                l.ToEmail,
                l.RedirectedTo,
                l.Ok,
                l.Error,
                l.EmployeeId,
                l.Wiedervorlage,
                MaNummer = l.EmployeeId == null ? null
                    : _db.Employees.Where(e => e.Id == l.EmployeeId).Select(e => e.EmployeeNumber).FirstOrDefault(),
                MaName = l.EmployeeId == null ? null
                    : _db.Employees.Where(e => e.Id == l.EmployeeId)
                        .Select(e => ((e.FirstName ?? "") + " " + (e.LastName ?? "")).Trim()).FirstOrDefault(),
            })
            .ToListAsync();

        DateTime? WiederholungAm(string? adresse)
            => adresse != null && offenBis.TryGetValue(adresse, out var t) ? t : null;

        var rows = roh2.Select(l => new
        {
            l.Id, l.CreatedAt, l.ToEmail, l.RedirectedTo, l.Ok, l.Error, l.EmployeeId,
            l.Wiedervorlage, l.MaNummer, l.MaName,
            spaeterZugestellt = !l.Ok && l.ToEmail != null && spaeterSet.Contains(l.ToEmail),
            wiederholungAm   = l.Ok ? null : WiederholungAm(l.ToEmail),
        }).ToList();

        return Ok(new
        {
            zeilen = rows,
            hergeleitet,
            verweisVorhanden = rows.Count > 0,
            spaeterZugestellt = spaeterSet.Count,
            wiederholungOffen = offenBis.Count,
        });
    }

    private int? GetCurrentUserId()
    {
        var sub = User.FindFirstValue("sub")
               ?? User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);
        return int.TryParse(sub, out var id) ? id : null;
    }

    /// <summary>«3,7,12» → Liste. Leere und unlesbare Einträge fallen weg.</summary>
    private static List<int> ParseIds(string? csv)
        => (csv ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => int.TryParse(t, out var v) ? v : 0)
            .Where(v => v > 0)
            .Distinct()
            .ToList();
}
