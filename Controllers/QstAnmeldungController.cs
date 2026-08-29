using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace HrSystem.Controllers;

/// <summary>
/// Generiert das amtliche QST-Anmeldeformular für einen Mitarbeiter.
/// Aktuell SO-Template; weitere Kantone können über den ?kanton=-Parameter
/// nachgezogen werden, wenn Walter sie braucht.
/// </summary>
[ApiController]
[Route("api/qst-anmeldung")]
[Authorize]
public class QstAnmeldungController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly QstAnmeldungPdfService _pdf;

    public QstAnmeldungController(AppDbContext db, QstAnmeldungPdfService pdf)
    {
        _db = db;
        _pdf = pdf;
    }

    /// <summary>
    /// Generiert das QST-Anmeldeformular als PDF.
    /// Alle Parameter sind optional — Standard:
    ///   • Filiale wird aus dem aktiven Arbeitsverhältnis des MA ermittelt.
    ///   • Kanton-Template wird aus dem Wohnkanton des MA (employee.CantonCode)
    ///     abgeleitet. Aktuell existiert nur die SO-Vorlage; sobald weitere
    ///     Kantons-Templates dazukommen, wird der Wohnkanton automatisch
    ///     auf das passende Template gemappt.
    /// Manuelles Überschreiben via Query-Parametern bleibt für Spezialfälle möglich.
    /// </summary>
    [HttpGet("{employeeId}/pdf")]
    public async Task<IActionResult> Generate(
        int employeeId,
        [FromQuery] int? companyProfileId,
        [FromQuery] string? kanton = null,
        // Overrides für die Tarif-relevanten Ja/Nein-Felder. Werden vom HR-
        // Frontend mitgegeben, wenn beim MA kein aktiver QST-Eintrag existiert
        // (Review-Modal vor PDF-Generierung). Wenn nicht gesetzt → Werte aus
        // dem QST-Eintrag bzw. Default-Logik (Walter: "weder Ja noch Nein"
        // wenn unbekannt).
        [FromQuery] bool? livesInKonkubinat = null,
        [FromQuery] bool? hasJointParentalCare = null,
        [FromQuery] bool? paysAlimonyAdultChildren = null,
        [FromQuery] bool? hasHigherIncomeThanPartner = null,
        [FromQuery] bool? isGrenzgaenger = null,
        [FromQuery] bool? isWochenaufenthalter = null,
        [FromQuery] bool? hasOtherEmployment = null,
        // Walter-Vorgabe 29.08.2026: Unterschrift WÄHLBAR — «Ich oder der
        // Geschäftsführer». Ohne Parameter = eingeloggter User (bisheriges
        // Verhalten). Mit Parameter: nur User mit hinterlegter Unterschrift
        // UND Zugriff auf die Filiale (bzw. admin) — die Auswahl ist eine
        // BEWUSSTE Entscheidung des Erstellers, kein stiller Default.
        [FromQuery] int? signerUserId = null)
    {
        var emp = await _db.Employees
            .Include(e => e.PermitType)
            .Include(e => e.NationalityRef)
            .Include(e => e.Employments)
            .FirstOrDefaultAsync(e => e.Id == employeeId);
        if (emp == null) return NotFound(new { error = "Mitarbeiter nicht gefunden." });

        // Schweizer Bürger sind nicht QST-pflichtig — keine Anmeldung möglich.
        // AUSNAHME (Vorprüfung 0a, Walter 29.08.2026): Wohnsitz im Ausland ⇒
        // QST-pflichtig auch als CH-Bürger/in ⇒ Anmeldung ERLAUBT.
        if (IsSwiss(emp) && !WohntImAusland(emp))
            return BadRequest(new { error = "Mitarbeiter ist Schweizer Bürger/in — keine QST-Anmeldung erforderlich." });

        // Aktive Employment für die Filiale (oder erste aktive)
        var employment = (companyProfileId.HasValue
                ? emp.Employments.FirstOrDefault(em => em.CompanyProfileId == companyProfileId.Value && em.IsActive)
                : null)
            ?? emp.Employments.FirstOrDefault(em => em.IsActive)
            ?? emp.Employments.FirstOrDefault();
        if (employment == null) return BadRequest(new { error = "Kein Arbeitsverhältnis gefunden." });

        var company = await _db.CompanyProfiles.FirstOrDefaultAsync(c => c.Id == employment.CompanyProfileId);
        if (company == null) return BadRequest(new { error = "Filiale nicht gefunden." });

        var family = await _db.EmployeeFamilyMembers
            .Where(f => f.EmployeeId == employeeId)
            .ToListAsync();

        var ehepartner = family.FirstOrDefault(f => string.Equals(f.MemberType, "Ehepartner", StringComparison.OrdinalIgnoreCase));
        var kinder     = family.Where(f => string.Equals(f.MemberType, "Kind", StringComparison.OrdinalIgnoreCase))
                               .OrderBy(f => f.DateOfBirth ?? DateTime.MaxValue)
                               .ToList();

        // Aktiver QST-Eintrag (versioniert): ValidFrom <= heute AND (ValidTo NULL OR ValidTo >= heute).
        // Wenn KEIN aktiver Eintrag vorhanden ist, werden die tarif-relevanten
        // Ja/Nein-Felder auf dem Formular bewusst leer gelassen
        // (Walter: "wenn in der qs keine angaben zum tarif vorhanden sind,
        //  dann bitte auch auf dem formular weder ja noch nein ankreuzen").
        var today = DateOnly.FromDateTime(DateTime.Today);
        var qst = await _db.EmployeeQuellensteuer
            .Where(q => q.EmployeeId == employeeId
                     && q.ValidFrom <= today
                     && (q.ValidTo == null || q.ValidTo >= today))
            .OrderByDescending(q => q.ValidFrom)
            .FirstOrDefaultAsync();

        // ── Wohnkanton bestimmen ─────────────────────────────────────────
        // Der Wohnkanton des MA ist die zentrale Bestimmungsgrösse für die
        // QST-Anmeldung — die Anmeldung geht ans Steueramt des Wohnkantons,
        // und die SSL-Nummer der Filiale gilt jeweils nur für einen Kanton.
        //
        // Reihenfolge:
        // 1. employee.CantonCode (aus der Wohnadresse abgeleitet — IMMER vorhanden)
        // 2. qst.Steuerkanton    (Fallback, falls Adresse keinen Kanton liefert
        //                          oder bewusst ein abweichender Steuerkanton
        //                          gepflegt wurde, z.B. Wochenaufenthalter)
        var wohnkanton = !string.IsNullOrWhiteSpace(emp.CantonCode)
            ? emp.CantonCode
            : qst?.Steuerkanton;

        // Kanton-Template: explizit über Query-Param, sonst Wohnkanton, sonst SO
        // (aktuell existiert nur die SO-Vorlage — sobald weitere Templates da sind,
        //  wird über diesen Wert das passende PDF im PdfService geladen).
        var templateKanton = !string.IsNullOrWhiteSpace(kanton)
            ? kanton!.ToUpperInvariant()
            : (wohnkanton ?? "SO");

        // SSL-Nummer für (Filiale, Wohnkanton des MA). Die SSL ist immer
        // kantonal — ein Arbeitgeber muss sich pro Kanton, in dem er QST-pflichtige
        // MA beschäftigt, separat anmelden.
        // EINZIGE Quelle seit 06.08.2026 (Walter): Lohndatenempfänger — QST-
        // Empfänger des Kantons, Mitgliednummer = SSL. BEWUSST KEIN Fallback
        // auf die Alt-Tabelle company_profile_ssl: deren Nummern sind evtl.
        // falsch erfasst (Walter 06.08.2026) — lieber leer + Check-Hinweis
        // als eine falsche SSL auf dem Behördenformular.
        string? sslNummer = null;
        if (!string.IsNullOrWhiteSpace(wohnkanton))
        {
            var heute = DateOnly.FromDateTime(DateTime.Today);
            sslNummer = await _db.CompanyProfileEmpfaengers.AsNoTracking()
                .Where(z => z.CompanyProfileId == company.Id && z.IsActive
                         && z.Empfaenger!.Art == "QST"
                         && z.Empfaenger!.KantonCode == wohnkanton
                         && (z.GueltigAb == null || z.GueltigAb <= heute))
                .OrderByDescending(z => z.GueltigAb)
                .Select(z => z.Mitgliednummer)
                .FirstOrDefaultAsync();
        }

        // HR-Verantwortliche/r als Kontaktperson auf dem Formular —
        // identisches Pattern wie Zwischenverdienst-Controller:
        // 1. User mit UserBranchAccess.Role = "HR_VERANTWORTLICH" für diese Filiale
        // 2. Sonst: User mit IsDefault=true für diese Filiale
        // 3. Sonst: erster User mit Branch-Zugriff auf diese Filiale
        var branchUsers = await _db.UserBranchAccesses
            .Include(uba => uba.User)
            .Where(uba => uba.CompanyProfileId == company.Id && uba.User.IsActive)
            .ToListAsync();
        // Walter-Vorgabe: NIE Geschäftsführer als HR-Ansprechperson auf dem
        // Formular. Reihenfolge: explizite HR-Rolle → IsHrTeam-Flag → IsDefault.
        var signatoryUba = branchUsers.FirstOrDefault(uba => uba.Role == "HR_VERANTWORTLICH")
                        ?? branchUsers.FirstOrDefault(uba => uba.User.IsHrTeam)
                        ?? branchUsers.FirstOrDefault(uba => uba.IsDefault);
        var hrUser = signatoryUba?.User;

        var overrides = new QstFormOverrides {
            LivesInKonkubinat          = livesInKonkubinat,
            HasJointParentalCare       = hasJointParentalCare,
            PaysAlimonyAdultChildren   = paysAlimonyAdultChildren,
            HasHigherIncomeThanPartner = hasHigherIncomeThanPartner,
            IsGrenzgaenger             = isGrenzgaenger,
            IsWochenaufenthalter       = isWochenaufenthalter,
            HasOtherEmployment         = hasOtherEmployment,
        };
        var data = MapToDto(emp, employment, company, ehepartner, kinder, hrUser, qst, sslNummer, overrides);

        // AG-Unterschrift: Standard = eingeloggter User (wer das PDF erzeugt,
        // unterschreibt). Walter-Vorgabe 29.08.2026: bei der QST-Anmeldung
        // WÄHLBAR («Ich oder der Geschäftsführer») — der gewählte User muss
        // eine hinterlegte Unterschrift UND Zugriff auf die Filiale haben
        // (oder admin sein); die bewusste Auswahl ersetzt hier den strikten
        // Nur-eingeloggter-User-Grundsatz. Der getippte HR-Verantwortliche-
        // Name oben im Formular bleibt unverändert.
        byte[]? signaturePng = null;
        string? signerName = null;
        var loggedInIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        int.TryParse(loggedInIdStr, out var loggedInId);
        var effectiveSignerId = signerUserId ?? (loggedInId > 0 ? loggedInId : (int?)null);
        if (effectiveSignerId.HasValue)
        {
            var signerUser = await _db.AppUsers
                .Where(u => u.Id == effectiveSignerId.Value && u.IsActive)
                .Select(u => new { u.Id, u.SignaturePng, u.FirstName, u.LastName, u.Username, u.Role })
                .FirstOrDefaultAsync();
            if (signerUser != null)
            {
                // Fremd-Unterschrift nur mit Unterschrift + Filial-Bezug.
                bool fremd = signerUser.Id != loggedInId;
                bool zulaessig = !fremd
                    || (signerUser.SignaturePng != null
                        && (signerUser.Role == "admin"
                            || await _db.UserBranchAccesses.AnyAsync(uba =>
                                   uba.UserId == signerUser.Id && uba.CompanyProfileId == company.Id)));
                if (fremd && !zulaessig)
                    return BadRequest(new { error = "SIGNER_UNGUELTIG",
                        message = "Gewählte Unterschrift nicht möglich: der User braucht eine hinterlegte Unterschrift und Zugriff auf diese Filiale." });
                signaturePng = signerUser.SignaturePng;
                var fullName = $"{signerUser.FirstName} {signerUser.LastName}".Trim();
                signerName   = string.IsNullOrWhiteSpace(fullName) ? signerUser.Username : fullName;
            }
        }

        byte[] bytes;
        try
        {
            // Template-Kanton durchreichen — der PdfService lädt Assets/Forms/QstAnmeldung_{kanton}.pdf
            // (mit Fallback auf SO, solange wir nur dieses Template haben).
            bytes = _pdf.Generate(data, templateKanton, signaturePng, signerName);
        }
        catch (Exception ex)
        {
            return Problem("PDF konnte nicht erstellt werden: " + ex.Message);
        }

        var filename = $"QST-Anmeldung_{emp.LastName}_{emp.FirstName}.pdf";
        return File(bytes, "application/pdf", filename);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Walter-Vorgabe 29.08.2026: wählbare AG-Unterschrift («Ich oder der
    // Geschäftsführer»). Liefert alle aktiven User MIT hinterlegter
    // Unterschrift, die admin sind ODER Zugriff auf die Filiale haben —
    // fürs Dropdown auf der QST-Anmeldungs-Seite (Default bleibt «ich»).
    // ════════════════════════════════════════════════════════════════════════
    [HttpGet("signers")]
    public async Task<IActionResult> GetSigners([FromQuery] int? companyProfileId)
    {
        var users = await _db.AppUsers
            .Where(u => u.IsActive && u.SignaturePng != null)
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Username, u.Role })
            .ToListAsync();

        HashSet<int> branchUserIds = new();
        if (companyProfileId.HasValue)
        {
            branchUserIds = (await _db.UserBranchAccesses
                .Where(uba => uba.CompanyProfileId == companyProfileId.Value)
                .Select(uba => uba.UserId)
                .ToListAsync()).ToHashSet();
        }

        var result = users
            .Where(u => !companyProfileId.HasValue || u.Role == "admin" || branchUserIds.Contains(u.Id))
            .Select(u =>
            {
                var name = $"{u.FirstName} {u.LastName}".Trim();
                return new { id = u.Id, name = string.IsNullOrWhiteSpace(name) ? u.Username : name };
            })
            .OrderBy(x => x.name)
            .ToList();
        return Ok(result);
    }

    // ════════════════════════════════════════════════════════════════════════
    // VALIDIERUNG: prüft ob alle Pflichtfelder für die QST-Anmeldung beim MA
    // hinterlegt sind. Wird vom Frontend VOR dem PDF-Generieren aufgerufen.
    // Liefert eine Liste fehlender Punkte mit Section-Hinweis, damit das
    // Frontend direkt zum richtigen Sub-Tab navigieren kann.
    // ════════════════════════════════════════════════════════════════════════
    [HttpGet("{employeeId}/validate")]
    public async Task<IActionResult> Validate(
        int employeeId,
        [FromQuery] int? companyProfileId)
    {
        var emp = await _db.Employees
            .Include(e => e.PermitType)
            .Include(e => e.NationalityRef)
            .Include(e => e.Employments)
            .FirstOrDefaultAsync(e => e.Id == employeeId);
        if (emp == null) return NotFound(new { error = "Mitarbeiter nicht gefunden." });

        // ── Schweizer? Dann keine QST-Pflicht und keine Anmeldung nötig ───
        // Walter-Vorgabe: Quellensteuer ist immer Pflicht AUSSER für Schweizer
        // Bürger. Erkennung über Nationalität (NationalityRef.Code = "CH"
        // oder Nationality-Text enthält "Schweiz").
        // Vorprüfung 0a (Walter 29.08.2026): CH-Bürger/in nur befreit, wenn
        // AUCH in der Schweiz ansässig — Auslands-Wohnsitz ⇒ pflichtig.
        bool isSwiss = IsSwiss(emp) && !WohntImAusland(emp);
        if (isSwiss)
        {
            return Ok(new {
                ok           = true,
                qstRequired  = false,
                reason       = "Mitarbeiter ist Schweizer Bürger/in — keine QST-Anmeldung erforderlich.",
                missing      = new object[0]
            });
        }

        var missing = new List<object>();
        void Add(string label, string section, string? hint = null)
            => missing.Add(new { label, section, hint });

        // ── Personalien ────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(emp.Gender))
            Add("Geschlecht",     "personalien");
        if (!emp.DateOfBirth.HasValue)
            Add("Geburtsdatum",   "personalien");
        if (string.IsNullOrWhiteSpace(emp.SocialSecurityNumber))
            Add("AHV-Nummer",     "personalien");
        if (emp.NationalityRef == null && string.IsNullOrWhiteSpace(emp.Nationality))
            Add("Nationalität",   "personalien");
        if (string.IsNullOrWhiteSpace(emp.MaritalStatus))
            Add("Zivilstand",     "personalien");

        // Adresse muss komplett sein (für Wohnsitz-Kanton-Ableitung)
        if (string.IsNullOrWhiteSpace(emp.Street))
            Add("Adresse: Strasse",  "personalien");
        if (string.IsNullOrWhiteSpace(emp.ZipCode))
            Add("Adresse: PLZ",      "personalien");
        if (string.IsNullOrWhiteSpace(emp.City))
            Add("Adresse: Ort",      "personalien");
        if (string.IsNullOrWhiteSpace(emp.CantonCode))
            Add("Wohnkanton",        "personalien",
                "Kanton wird normalerweise via PLZ-Lookup automatisch gesetzt — bitte Adresse prüfen.");

        // ── Familie: nur wenn verheiratet/eingetragene Partnerschaft ───
        var isMarried = !string.IsNullOrWhiteSpace(emp.MaritalStatus)
            && (emp.MaritalStatus.Equals("verheiratet", StringComparison.OrdinalIgnoreCase)
             || emp.MaritalStatus.StartsWith("eingetragene", StringComparison.OrdinalIgnoreCase)
             || emp.MaritalStatus.Equals("getrennt", StringComparison.OrdinalIgnoreCase));
        if (isMarried)
        {
            var ehepartner = await _db.EmployeeFamilyMembers
                .Where(f => f.EmployeeId == employeeId
                         && f.MemberType.ToLower() == "ehepartner")
                .FirstOrDefaultAsync();
            if (ehepartner == null)
            {
                Add("Ehepartner-Eintrag", "familie",
                    "Bitte unter Familie den Ehepartner mit Name, Vorname, Geburtsdatum und AHV-Nr. erfassen.");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(ehepartner.LastName))   Add("Ehepartner: Nachname",     "familie");
                if (string.IsNullOrWhiteSpace(ehepartner.FirstName))  Add("Ehepartner: Vorname",      "familie");
                if (string.IsNullOrWhiteSpace(ehepartner.Gender))     Add("Ehepartner: Geschlecht",   "familie");
                if (!ehepartner.DateOfBirth.HasValue)                 Add("Ehepartner: Geburtsdatum", "familie");
                // Walter-Vorgabe 20.08.2026: Nationalität + Erwerbstätig-Frage
                // sind fürs Anmeldeformular Pflicht (Tarif B vs. C, EP-Erwerb-
                // Kreuz auf dem Kantonsformular).
                if (ehepartner.NationalityId == null)                 Add("Ehepartner: Nationalität", "familie");
                if (ehepartner.Erwerbstaetig == null)
                    Add("Ehepartner: erwerbstätig Ja/Nein", "familie",
                        "Bitte im Familie-Tab beim Ehepartner die Erwerbstätig-Frage beantworten (entscheidet Tarif B oder C).");
                else if (ehepartner.Erwerbstaetig == true && string.IsNullOrWhiteSpace(ehepartner.ArbeitgeberName))
                    Add("Ehepartner: Arbeitgeber", "familie",
                        "Der Ehepartner ist erwerbstätig — bitte Arbeitgeber-Name (und Arbeitsort) erfassen. " +
                        "Bei Erwerbs-/Ersatzeinkommen im Ausland (z.B. Militärsold, Rente) den Sachverhalt " +
                        "ins Arbeitgeber-Feld schreiben, z.B. «Militärdienst Ukraine (Ersatzeinkommen)».");
            }
        }

        // ── Aktiver QST-Eintrag (Pflicht — Walter-Vorgabe) ─────────────
        var today = DateOnly.FromDateTime(DateTime.Today);
        var hasActiveQst = await _db.EmployeeQuellensteuer
            .AnyAsync(q => q.EmployeeId == employeeId
                        && q.ValidFrom <= today
                        && (q.ValidTo == null || q.ValidTo >= today));
        if (!hasActiveQst)
        {
            Add("Aktiver QST-Eintrag", "quellensteuer",
                "Bitte einen aktiven QST-Eintrag erfassen — auch wenn die behördliche Bestätigung noch fehlt, lieber vorläufig mit dem höchsten Tarif anlegen als gar nicht.");
        }

        // ── Aktives Arbeitsverhältnis für die Filiale ──────────────────
        var employment = (companyProfileId.HasValue
                ? emp.Employments.FirstOrDefault(em => em.CompanyProfileId == companyProfileId.Value && em.IsActive)
                : null)
            ?? emp.Employments.FirstOrDefault(em => em.IsActive);
        if (employment == null)
        {
            Add("Aktives Arbeitsverhältnis", "vertraege",
                "Bitte aktiven Vertrag im Vertrags-Modul erfassen.");
        }

        // ── SSL-Nummer (Filiale × Wohnkanton) ──────────────────────────
        // Quelle: QST-Lohndatenempfänger des Kantons (Fallback Alt-Tabelle).
        if (employment != null && !string.IsNullOrWhiteSpace(emp.CantonCode))
        {
            var sslExists = await _db.CompanyProfileEmpfaengers.AnyAsync(z =>
                z.CompanyProfileId == employment.CompanyProfileId && z.IsActive
                && z.Empfaenger!.Art == "QST"
                && z.Empfaenger!.KantonCode == emp.CantonCode
                && z.Mitgliednummer != null && z.Mitgliednummer != "");
            if (!sslExists)
            {
                Add($"SSL-Nummer für Wohnkanton {emp.CantonCode}", "filiale-ssl",
                    "Filiale → Tab «Lohndaten Empfänger» → Quellensteuer-Empfänger des Kantons zuordnen und SSL-Nummer eintragen.");
            }
        }

        return Ok(new {
            ok = missing.Count == 0,
            qstRequired = true,
            missing
        });
    }

    /// <summary>
    /// Prüft ob ein MA Schweizer Bürger/in ist. Berücksichtigt sowohl die
    /// strukturierte NationalityRef.Code (Standard "CH") als auch das alte
    /// Freitext-Feld Nationality (für Legacy-Daten).
    /// </summary>
    private static bool IsSwiss(Employee emp)
    {
        if (emp.NationalityRef != null
            && string.Equals(emp.NationalityRef.Code, "CH", StringComparison.OrdinalIgnoreCase))
            return true;
        var nat = emp.Nationality?.ToLowerInvariant() ?? "";
        return nat == "ch" || nat.Contains("schweiz") || nat == "swiss" || nat == "schweizer";
    }

    /// <summary>
    /// Vorprüfung 0a (Schulung / Walter 29.08.2026): Hauptwohnsitz im Ausland
    /// ⇒ «Person ohne steuerrechtlichen Wohnsitz CH» ⇒ QST-pflichtig auch als
    /// CH-Bürger/in — der Schweizer-Block darf dann NICHT greifen.
    /// </summary>
    private static bool WohntImAusland(Employee emp)
    {
        var land = (emp.Country ?? "").Trim();
        return land.Length > 0
            && !land.Equals("CH", StringComparison.OrdinalIgnoreCase)
            && !land.Equals("Schweiz", StringComparison.OrdinalIgnoreCase);
    }

    // ── Mapping Datenbank → DTO ────────────────────────────────────────────
    // Ja/Nein-Konvention im SO-PDF: bei den meisten Radio-Feldern ist "1" = Ja, "0" = Nein.
    // (z.B. Trennung, Andere Erwerbstätigkeit, EP-Erwerbstätigkeit)
    private const string Ja            = "1";
    private const string Nein          = "0";

    // AUSNAHME: der gesamte Elterntarif-Block ist UMGEKEHRT codiert ("0" = Ja, "1" = Nein).
    // Das gilt für alle 5 Radios in der Sektion "Abklärung Elterntarif":
    //   • Kinder            (Leben Sie mit Kindern im gleichen Haushalt?)
    //   • Konkubinat        (Leben Sie im Konkubinat?)
    //   • Elterliche-Sorge  (Üben Sie die elterliche Sorge aus?)
    //   • Unterhaltszahlung (Zahlen Sie Unterhalt für volljährige Kinder?)
    //   • Höheres-Bruttoeinkommen (Erzielen Sie das höhere Bruttoeinkommen?
    //                             "0" = Ja, "1" = Nein, Konkubinatspartner/in)
    // Hintergrund: das "Ja" ist im SO-Formular jeweils mit einem zusätzlichen
    // Eingabefeld kombiniert (Anzahl Kinder bzw. der Hinweistext beim Einkommen),
    // deshalb wurde es als erstes Radio mit Index 0 angelegt.
    private const string Elt_Ja        = "0";
    private const string Elt_Nein      = "1";

    private class QstFormOverrides
    {
        public bool? LivesInKonkubinat          { get; set; }
        public bool? HasJointParentalCare       { get; set; }
        public bool? PaysAlimonyAdultChildren   { get; set; }
        public bool? HasHigherIncomeThanPartner { get; set; }
        public bool? IsGrenzgaenger             { get; set; }
        public bool? IsWochenaufenthalter       { get; set; }
        public bool? HasOtherEmployment         { get; set; }
    }

    private static QstAnmeldungData MapToDto(
        Employee emp,
        Employment employment,
        CompanyProfile company,
        EmployeeFamilyMember? ehepartner,
        List<EmployeeFamilyMember> kinder,
        AppUser? hrUser,
        EmployeeQuellensteuer? qst,
        string? sslNummer,
        QstFormOverrides? overrides = null)
    {
        // Helper: Override hat Vorrang vor QST-Eintrag. Liefert null wenn weder
        // Override noch QST-Eintrag vorhanden (= "weder Ja noch Nein" auf Form).
        string? Resolve(bool? overrideVal, bool? entryVal) =>
            overrideVal.HasValue ? (overrideVal.Value ? Elt_Ja : Elt_Nein)
            : entryVal.HasValue  ? (entryVal.Value    ? Elt_Ja : Elt_Nein)
            : null;
        // Kontaktperson = HR-Verantwortliche/r für diese Filiale.
        // Reihenfolge: "Vorname Nachname" (so wie's gesprochen wird).
        var hrName  = hrUser != null
            ? JoinNonEmpty(" ", hrUser.FirstName, hrUser.LastName).Trim()
            : null;
        if (string.IsNullOrWhiteSpace(hrName)) hrName = hrUser?.Username;
        // Telefon: Filiale bevorzugt (zentrale Nummer ist sinnvoller für Behörden);
        // E-Mail: persönliche der HR-Person bevorzugt (für direkten Kontakt).
        var hrPhone = !string.IsNullOrWhiteSpace(company.Phone)  ? company.Phone : hrUser?.Phone;
        var hrEmail = !string.IsNullOrWhiteSpace(hrUser?.Email)  ? hrUser?.Email : company.Email;

        return new QstAnmeldungData
        {
            // Arbeitgeber — Kontakt = HR-Verantwortliche
            SslNummer       = sslNummer,                          // QST-Schuldner-Nr. (Filiale × Wohnsitzkanton MA)
            UidNummer       = company.UidNummer,
            Firma           = company.CompanyName + (string.IsNullOrWhiteSpace(company.BranchName) ? "" : ", " + company.BranchName),
            Adresse         = JoinNonEmpty(" ", company.Street, company.HouseNumber),
            PlzOrtKanton    = JoinNonEmpty(" ", company.ZipCode, company.City),
            Kontaktperson   = hrName,
            Telefon         = hrPhone,
            Email           = hrEmail,

            // Quellensteuerpflichtige/r
            QaGeschlecht    = MapGeschlecht(emp.Gender),
            QaName          = emp.LastName,
            QaVorname       = emp.FirstName,
            QaStrasse       = emp.Street,
            QaPlzOrtLand    = JoinNonEmpty(" ", emp.ZipCode, emp.City, emp.Country ?? "CH"),
            QaGeburtsdatum  = emp.DateOfBirth?.ToString("dd.MM.yyyy"),
            QaNationalitaet = emp.NationalityRef?.Code ?? emp.Nationality,
            QaBewilligung   = emp.PermitType?.Code,
            QaSvNummer      = emp.SocialSecurityNumber,

            // Zivilstand + Trennung
            // Sonderfall "getrennt": rechtlich ist man bis zur Scheidung weiterhin
            // verheiratet. Auf den kantonalen QST-Formularen gibt's "getrennt" nie
            // als eigenständigen Zivilstand — wir bilden's daher als verheiratet
            // + Trennung-Häkchen Ja ab.
            // Walter-Vorgabe 12.07.2026: das Trennung-Häkchen NUR setzen, wenn der
            // Zivilstand wirklich «getrennt» ist — sonst GAR KEIN Kreuz (auch nicht
            // «Nein»; null → alle Kanton-Mapper überspringen das Feld). Vorher
            // triggerte auch ein verwaistes SeparatedSince fälschlich «Ja»
            // (z.B. bei ledigen MA).
            Zivilstand      = MapZivilstand(emp.MaritalStatus == "getrennt" ? "verheiratet" : emp.MaritalStatus),
            GetrenntJaNein  = emp.MaritalStatus == "getrennt" ? Ja : null,
            DatumZivilstand = emp.MaritalStatusSince?.ToString("dd.MM.yyyy"),
            Konfession      = MapKonfession(emp.Religion),

            // Aufenthaltsadresse — gleich wie Wohnadresse, falls keine separate gepflegt
            AufenthaltAdresse      = emp.Street,
            AufenthaltPlzOrtKanton = JoinNonEmpty(" ", emp.ZipCode, emp.City, emp.CantonCode),

            // Beruf
            StellenAntrittDatum  = employment.ContractStartDate.ToString("dd.MM.yyyy"),
            BruttolohnMonat      = FormatChf(employment.MonthlySalary),
            ArbeitspensumProzent = employment.EmploymentPercentage?.ToString("0.##"),
            // Grenzgänger / Wochenaufenthalter — Override hat Vorrang, sonst
            // QST-Eintrag, sonst leer.
            BerufFlag            = (overrides?.IsGrenzgaenger == true)        ? "0"
                                  : (overrides?.IsWochenaufenthalter == true) ? "1"
                                  : (overrides?.IsGrenzgaenger == false && overrides?.IsWochenaufenthalter == false) ? null
                                  : qst == null                              ? null
                                  : qst.IsGrenzgaenger                       ? "0"
                                  : qst.IsWochenaufenthalter                 ? "1"
                                  : null,

            // Andere Erwerbstätigkeit — Override hat Vorrang, sonst aus der
            // QST-Erfassung (Walter 25.08.2026: Adresse des weiteren AG wird
            // dort erfasst und fliesst hier aufs Behördenformular).
            HatAndereErwerbJaNein = (overrides?.HasOtherEmployment ?? qst?.WeitereBeschaftigungen ?? false) ? Ja : Nein,
            AndereArbeitgeberName          = qst?.WeitereAgName,
            AndereArbeitgeberStrasse       = qst?.WeitereAgStrasse,
            AndereArbeitgeberPlzOrtKanton  = qst == null ? null
                : (string.Join(" ", new[] { qst.WeitereAgPlz, qst.WeitereAgOrt, qst.WeitereAgKanton }
                    .Where(s => !string.IsNullOrWhiteSpace(s))).Trim() is { Length: > 0 } wagOrt ? wagOrt : null),
            AndereArbeitgeberLand          = qst?.WeitereAgLand,
            GesamtPensumProzent            = qst?.GesamtpensumWeitereAg?.ToString("0.##"),

            // Ehepartner
            EpGeschlecht    = ehepartner != null ? MapGeschlecht(ehepartner.Gender) : null,
            EpName          = ehepartner?.LastName,
            EpVorname       = ehepartner?.FirstName,
            EpGeburtsdatum  = ehepartner?.DateOfBirth?.ToString("dd.MM.yyyy"),
            EpSvNummer      = ehepartner?.SocialSecurityNumber,
            // Walter-Vorgabe 20.08.2026: EP-Erwerbstätig kommt aus dem echten
            // Familien-Feld (vorher fix «Nein» — bei Tarif C faktisch falsch
            // auf dem Behördenformular). NULL = Frage offen → kein Kreuz.
            EpHatErwerbJaNein = ehepartner?.Erwerbstaetig == true  ? Ja
                              : ehepartner?.Erwerbstaetig == false ? Nein
                              : null,

            // Kinder
            AnzahlKinder    = kinder.Count > 0 ? kinder.Count.ToString() : null,
            Kinder          = kinder.Take(4).Select(k =>
                                JoinNonEmpty(" / ",
                                    JoinNonEmpty(" ", k.LastName, k.FirstName),
                                    k.DateOfBirth?.ToString("dd.MM.yyyy")
                                )).ToList(),

            // ── Abklärung Elterntarif ───────────────────────────────────────
            // Der ganze Elterntarif-Block verwendet die invertierte Elt-Konvention
            // ("0" = Ja, "1" = Nein) — siehe Kommentar oben bei den Konstanten.

            // Kinder im Haushalt → richtet sich nach hinterlegten Familienmitgliedern
            // (unabhängig vom QST-Eintrag, da Kinder MA-Stammdaten sind).
            LebenKinderImHaushaltJaNein     = kinder.Count > 0 ? Elt_Ja : Elt_Nein,

            // Die folgenden 4 Felder: Override hat Vorrang, sonst QST-Eintrag,
            // sonst null (= weder Ja noch Nein angekreuzt — Walter-Anforderung).
            LebtImKonkubinatJaNein          = Resolve(overrides?.LivesInKonkubinat,          qst?.LivesInKonkubinat),
            UebtElterlicheSorgeJaNein       = Resolve(overrides?.HasJointParentalCare,       qst?.HasJointParentalCare),
            ZahltUnterhaltVolljaehrigJaNein = Resolve(overrides?.PaysAlimonyAdultChildren,   qst?.PaysAlimonyAdultChildren),
            HoeheresBruttoEinkommenJaNein   = Resolve(overrides?.HasHigherIncomeThanPartner, qst?.HasHigherIncomeThanPartner),

            // Ort/Datum vorausgefüllt
            OrtDatum = $"{company.City ?? "Meggen"}, {DateTime.Today:dd.MM.yyyy}",
        };
    }

    // ── Mapping-Helpers ────────────────────────────────────────────────────
    private static string? MapGeschlecht(string? g) =>
        g?.ToLowerInvariant() switch
        {
            "m" or "male" or "männlich" or "maennlich" or "herr" => "0",
            "f" or "w" or "female" or "weiblich" or "frau"       => "1",
            "divers" or "diverse" or "andere" or "other" or "x" or "d" => null,
            _ => null,
        };

    /// <summary>
    /// Form-Reihenfolge auf SO-Formular:
    ///   "0"=ledig | "1"=geschieden | "2"=verwitwet
    ///   "3"=verheiratet | "4"=eingetr. Partnerschaft | "5"=aufgel. Partnerschaft
    /// </summary>
    private static string? MapZivilstand(string? z) =>
        z?.ToLowerInvariant() switch
        {
            "ledig"                                   => "0",
            "geschieden"                              => "1",
            "verwitwet"                               => "2",
            "verheiratet"                             => "3",
            "eingetragene_partnerschaft"
                or "eingetragenepartnerschaft"        => "4",
            "aufgeloeste_partnerschaft"
                or "aufgelostepartnerschaft"
                or "aufgeloestepartnerschaft"         => "5",
            _ => null,
        };

    /// <summary>
    /// "0"=evang.-reformiert | "1"=röm.-katholisch | "2"=christ-katholisch | "3"=andere/keine.
    /// Default für leere oder unbekannte Werte: "3" (andere/keine) — Walter-Vorgabe:
    /// "wenn nicht bei MA hinterlegt, dann andere/keine ankreuzen".
    /// </summary>
    private static string? MapKonfession(string? k) =>
        k?.ToLowerInvariant() switch
        {
            "evangelisch_reformiert" or "evangelisch-reformiert" or "evang"
                or "reformiert" or "evangelisch"                          => "0",
            "roemisch_katholisch" or "römisch-katholisch"
                or "rk" or "roemisch"                                     => "1",
            "christ_katholisch" or "christkatholisch" or "christ-katholisch" => "2",
            // Default = andere/keine: bewusst inkl. NULL und allem Unbekannten,
            // damit das Formular nie ohne Konfessions-Kreuz rausgeht.
            _ => "3",
        };

    private static string? FormatChf(decimal? v) =>
        v.HasValue ? v.Value.ToString("N2").Replace(",", "'") : null;

    private static string JoinNonEmpty(string sep, params string?[] parts) =>
        string.Join(sep, parts.Where(p => !string.IsNullOrWhiteSpace(p)));
}
