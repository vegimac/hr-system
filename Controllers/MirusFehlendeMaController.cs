using System.Globalization;
using System.Text.RegularExpressions;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HrSystem.Controllers;

/// <summary>
/// HR-Hub → Kontrolle → «Mirus-Abgleich: fehlende MA» (Walter 03.09.2026).
///
/// Doppelspur-Kontrolle in die andere Richtung als der Adress-Vergleich:
/// Welche AKTIVEN OneCrew-Mitarbeitenden der Sidebar-Filiale fehlen in Mirus
/// noch ganz? Quelle ist der Mirus-Personalexport (XLS/XLSX) mit den Spalten
/// Personal-Nr. · Name · Vorname · Geb.-Datum · AHV-Nummer · Eintritt ·
/// Austritt · Kostenstelle · Strasse · PLZ · Ort (Reihenfolge egal, es
/// wird per Kopfzeile gesucht; die Mirus «Adressliste» mit «Pers. Nr.»
/// funktioniert ebenfalls).
///
/// Match-Kaskade pro Mirus-Zeile: Personalnummer (inkl. alte Nummern /
/// Aliase) → AHV-Nummer → Vorname+Nachname+Geburtsdatum → Vorname+Nachname
/// (nur wenn eindeutig). Was danach in OneCrew übrig bleibt, fehlt in Mirus.
///
/// Ergebnis: pro fehlendem MA ALLE Angaben, die man zum Erfassen in Mirus
/// braucht (Personalien, Adresse/Kontakt, Bewilligung, Vertrag, Bank,
/// Quellensteuer, Familie) — als JSON für die Seite und als PDF (ein MA pro
/// Seite, Namen NICHT anonymisiert — das Blatt ist eine Erfassungshilfe,
/// kein Versand-Dokument). Reine Auswertung, es wird nichts geschrieben.
/// </summary>
[Authorize(Roles = "admin,superuser")]
[ApiController]
[Route("api/imports/mirus-fehlende-ma")]
public class MirusFehlendeMaController : ControllerBase
{
    private readonly AppDbContext _db;
    public MirusFehlendeMaController(AppDbContext db) => _db = db;

    // ─────────────────────────── DTOs ───────────────────────────

    public class VertragInfo
    {
        public string? Modell { get; set; }
        public string? Lohnart { get; set; }
        public string? Funktion { get; set; }
        public string? FunktionText { get; set; }
        public string? Vertragstyp { get; set; }
        public DateTime? Von { get; set; }
        public DateTime? Bis { get; set; }
        public decimal? PensumProzent { get; set; }
        public decimal? Wochenstunden { get; set; }
        public decimal? GarantierteStunden { get; set; }
        public decimal? Monatslohn { get; set; }
        public decimal? MonatslohnFte { get; set; }
        public decimal? Stundenlohn { get; set; }
        public string? Ferienzahlung { get; set; }
        public int? ProbezeitMonate { get; set; }
        public DateTime? ProbezeitBis { get; set; }
        public bool Aktiv { get; set; }
    }

    public class BankInfo
    {
        public string? Iban { get; set; }
        public string? Bank { get; set; }
        public string? Kontoinhaber { get; set; }
        public bool Hauptbank { get; set; }
        public string? Aufteilung { get; set; }
    }

    public class QstInfo
    {
        public bool Pflichtig { get; set; }
        public string? Tarif { get; set; }
        public string? TarifText { get; set; }
        public string? Kanton { get; set; }
        public string? Gemeinde { get; set; }
        public int? GemeindeBfs { get; set; }
        public bool Kirchensteuer { get; set; }
        public int? Kinder { get; set; }
        public decimal? Prozent { get; set; }
        public DateOnly? GueltigAb { get; set; }
        public string? Hinweis { get; set; }
    }

    public class FamilieInfo
    {
        public string? Typ { get; set; }
        public string? Vorname { get; set; }
        public string? Nachname { get; set; }
        public DateTime? Geburtsdatum { get; set; }
        public string? Ahv { get; set; }
        public bool ImHaushalt { get; set; }
        public bool InSchweiz { get; set; }
    }

    public class MaDetail
    {
        public int EmployeeId { get; set; }
        public string? Personalnummer { get; set; }
        public List<string> AlteNummern { get; set; } = new();
        public string? Anrede { get; set; }
        public string? Geschlecht { get; set; }
        public string? Vorname { get; set; }
        public string? Nachname { get; set; }
        public string? Ledigname { get; set; }
        public DateTime? Geburtsdatum { get; set; }
        public string? Ahv { get; set; }
        public string? Zivilstand { get; set; }
        public DateOnly? ZivilstandSeit { get; set; }
        public string? Nationalitaet { get; set; }
        public string? NationalitaetCode { get; set; }
        public string? Bewilligung { get; set; }
        public string? BewilligungText { get; set; }
        public DateOnly? BewilligungBis { get; set; }
        public string? Zemis { get; set; }
        public string? Konfession { get; set; }
        public string? Sprache { get; set; }
        public string? Heimatort { get; set; }

        public string? Strasse { get; set; }
        public string? Plz { get; set; }
        public string? Ort { get; set; }
        public string? Kanton { get; set; }
        public string? Land { get; set; }
        public string? Telefon { get; set; }
        public string? Telefon2 { get; set; }
        public string? Email { get; set; }

        public DateTime? Eintritt { get; set; }
        public string? KostenstelleVorschlag { get; set; }
        public bool LgavPflichtig { get; set; }
        public bool TeilzeitUnter8h { get; set; }
        public List<VertragInfo> Vertraege { get; set; } = new();
        public List<BankInfo> Banken { get; set; } = new();
        public QstInfo? Qst { get; set; }
        public List<FamilieInfo> Familie { get; set; } = new();
        /// <summary>Pflichtangaben, die in OneCrew selbst noch fehlen (AHV, Geburtsdatum, IBAN …).</summary>
        public List<string> Luecken { get; set; } = new();
    }

    public class MirusZeile
    {
        public int Zeile { get; set; }
        public string? Personalnummer { get; set; }
        public string? MirusPnr { get; set; }
        public string? Vorname { get; set; }
        public string? Nachname { get; set; }
        public DateTime? Geburtsdatum { get; set; }
        public string? Ahv { get; set; }
        public DateTime? Eintritt { get; set; }
        public DateTime? Austritt { get; set; }
        public string? Kostenstelle { get; set; }
        /// <summary>Gematchter OneCrew-MA (falls gefunden).</summary>
        public int? EmployeeId { get; set; }
        public string? OneCrewName { get; set; }
        public bool? OneCrewAktiv { get; set; }
        public string? MatchArt { get; set; } // NUMMER | AHV | NAME_GEB | NAME | —
        public string? Hinweis { get; set; }
    }

    public class ErgebnisDto
    {
        public string? Filiale { get; set; }
        public int MirusZeilen { get; set; }
        public int OneCrewAktiv { get; set; }
        public int Gematcht { get; set; }
        public int Fehlend { get; set; }
        public int NurMirus { get; set; }
        public int MirusAusgetreten { get; set; }
        public int OhneLohn { get; set; }
        public List<MaDetail> FehlendeMa { get; set; } = new();
        public List<MirusZeile> NurMirusZeilen { get; set; } = new();
        public List<MirusZeile> AusgetretenZeilen { get; set; } = new();
        public List<MirusZeile> GematchtZeilen { get; set; } = new();
    }

    // ─────────────────────────── Endpoints ───────────────────────────

    /// <summary>POST analyze — Auswertung als JSON, schreibt nichts.</summary>
    [HttpPost("analyze")]
    public async Task<IActionResult> Analyze(
        [FromForm] IFormFile file,
        [FromForm] int companyProfileId = 0,
        CancellationToken ct = default)
    {
        var (err, res) = await BuildAsync(file, companyProfileId, ct);
        if (err != null) return err;
        return Ok(res);
    }

    /// <summary>POST pdf — Erfassungsliste der fehlenden MA (ein MA pro Seite).</summary>
    [HttpPost("pdf")]
    public async Task<IActionResult> Pdf(
        [FromForm] IFormFile file,
        [FromForm] int companyProfileId = 0,
        CancellationToken ct = default)
    {
        var (err, res) = await BuildAsync(file, companyProfileId, ct);
        if (err != null) return err;

        QuestPDF.Settings.License = LicenseType.Community;
        var bytes = BuildPdf(res!);
        var code = await _db.CompanyProfiles.AsNoTracking()
            .Where(c => c.Id == companyProfileId)
            .Select(c => c.RestaurantCode)
            .FirstOrDefaultAsync(ct);
        var fn = $"Mirus-fehlende-MA_{(string.IsNullOrWhiteSpace(code) ? "Filiale" : code)}_{DateTime.Now:yyyyMMdd}.pdf"
            .Replace(' ', '_');
        return File(bytes, "application/pdf", fn);
    }

    // ─────────────────────────── Kern ───────────────────────────

    private async Task<(IActionResult? Error, ErgebnisDto? Result)> BuildAsync(
        IFormFile? file, int companyProfileId, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return (BadRequest(new { error = "Datei fehlt." }), null);
        if (companyProfileId <= 0)
            return (BadRequest(new { error = "Bitte zuerst links eine Filiale wählen." }), null);

        var branch = await _db.CompanyProfiles.AsNoTracking()
            .Where(c => c.Id == companyProfileId)
            .Select(c => new { c.Id, c.RestaurantCode, c.City, c.BranchName })
            .FirstOrDefaultAsync(ct);
        if (branch == null)
            return (BadRequest(new { error = "Filiale nicht gefunden." }), null);

        List<MirusZeile> mirusRows;
        try
        {
            await using var stream = file.OpenReadStream();
            mirusRows = ParseMirusExport(stream, file.FileName);
        }
        catch (Exception ex)
        {
            return (BadRequest(new { error = $"Datei konnte nicht gelesen werden: {ex.Message}" }), null);
        }
        if (mirusRows.Count == 0)
            return (BadRequest(new { error = "Keine Datenzeilen gefunden. Erwartet: Mirus-Personalexport mit Spalten «Personal-Nr.», «Name», «Vorname»." }), null);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var todayDt = DateTime.Today;

        // Alle nicht versteckten MA mit irgendeinem Vertrag in dieser Filiale
        // (auch Inaktive — damit «nur in Mirus»-Zeilen erklärt werden können).
        var emps = await _db.Employees.AsNoTracking()
            .Where(e => !e.IsHidden && e.Employments.Any(em => em.CompanyProfileId == companyProfileId))
            .Include(e => e.Employments).ThenInclude(em => em.JobGroup)
            .Include(e => e.NationalityRef)
            .Include(e => e.PermitType)
            .Include(e => e.NumberAliases)
            .ToListAsync(ct);

        bool AktivInFiliale(Employee e)
        {
            if (!e.IsActive) return false;
            if (e.ExitDate != null && e.ExitDate.Value.Date < todayDt) return false;
            return e.Employments.Any(em => em.CompanyProfileId == companyProfileId
                                        && (em.ContractEndDate == null || em.ContractEndDate.Value.Date >= todayDt));
        }

        var aktiv = emps.Where(AktivInFiliale).ToList();
        var ohneLohn = aktiv.Where(e => e.IsPayrollExcluded).ToList();
        var kandidaten = aktiv.Where(e => !e.IsPayrollExcluded).ToList();

        // ── Lookup-Tabellen über ALLE Filial-MA (auch Inaktive) ──
        var byNumber = new Dictionary<string, List<Employee>>();
        var byAhv = new Dictionary<string, List<Employee>>();
        var byNameDob = new Dictionary<string, List<Employee>>();
        var byName = new Dictionary<string, List<Employee>>();
        static void Add(Dictionary<string, List<Employee>> d, string key, Employee e)
        {
            if (key.Length == 0) return;
            if (!d.TryGetValue(key, out var l)) d[key] = l = new List<Employee>();
            if (!l.Contains(e)) l.Add(e);
        }
        foreach (var e in emps)
        {
            Add(byNumber, NormNumber(e.EmployeeNumber), e);
            foreach (var a in e.NumberAliases) Add(byNumber, NormNumber(a.Number), e);
            Add(byAhv, NormAhv(e.SocialSecurityNumber), e);
            var nk = NameKey(e.FirstName, e.LastName);
            Add(byName, nk, e);
            if (e.DateOfBirth != null) Add(byNameDob, nk + "|" + e.DateOfBirth.Value.ToString("yyyyMMdd"), e);
        }

        // ── Mirus-Zeilen matchen ──
        var matched = new Dictionary<int, MirusZeile>();
        var nurMirus = new List<MirusZeile>();
        var ausgetreten = new List<MirusZeile>();
        var gematchtZeilen = new List<MirusZeile>();

        foreach (var m in mirusRows)
        {
            Employee? hit = null;
            string? art = null;

            var nr = NormNumber(m.Personalnummer);
            if (nr.Length > 0 && byNumber.TryGetValue(nr, out var c1))
            {
                hit = Pick(c1, m); art = "NUMMER";
            }
            if (hit == null)
            {
                var ahv = NormAhv(m.Ahv);
                if (ahv.Length == 13 && byAhv.TryGetValue(ahv, out var c2)) { hit = Pick(c2, m); art = "AHV"; }
            }
            if (hit == null && m.Geburtsdatum != null)
            {
                var k = NameKey(m.Vorname, m.Nachname) + "|" + m.Geburtsdatum.Value.ToString("yyyyMMdd");
                if (byNameDob.TryGetValue(k, out var c3)) { hit = Pick(c3, m); art = "NAME_GEB"; }
                if (hit == null)
                {
                    // Vorname/Nachname in Mirus vertauscht?
                    var k2 = NameKey(m.Nachname, m.Vorname) + "|" + m.Geburtsdatum.Value.ToString("yyyyMMdd");
                    if (byNameDob.TryGetValue(k2, out var c3b)) { hit = Pick(c3b, m); art = "NAME_GEB"; }
                }
            }
            if (hit == null)
            {
                var k = NameKey(m.Vorname, m.Nachname);
                if (byName.TryGetValue(k, out var c4) && c4.Count == 1) { hit = c4[0]; art = "NAME"; }
            }

            if (hit == null)
            {
                m.MatchArt = "—";
                m.Hinweis = "Kein OneCrew-MA dieser Filiale gefunden (evtl. andere Filiale oder nie in OneCrew erfasst).";
                nurMirus.Add(m);
                continue;
            }

            m.EmployeeId = hit.Id;
            m.OneCrewName = $"{hit.FirstName} {hit.LastName}".Trim();
            m.OneCrewAktiv = AktivInFiliale(hit);
            m.MatchArt = art;
            if (art != "NUMMER")
                m.Hinweis = art switch
                {
                    "AHV" => "Personalnummer weicht ab — über AHV-Nummer zugeordnet.",
                    "NAME_GEB" => "Personalnummer/AHV weichen ab — über Name + Geburtsdatum zugeordnet.",
                    _ => "Nur über den Namen zugeordnet — bitte prüfen."
                };

            if (m.Austritt != null && m.Austritt.Value.Date < todayDt && m.OneCrewAktiv == true)
            {
                m.Hinweis = (m.Hinweis == null ? "" : m.Hinweis + " ") +
                            $"In Mirus per {m.Austritt:dd.MM.yyyy} ausgetreten, in OneCrew aber aktiv — in Mirus reaktivieren/prüfen.";
                ausgetreten.Add(m);
            }
            else if (m.OneCrewAktiv == false)
            {
                m.Hinweis = (m.Hinweis == null ? "" : m.Hinweis + " ") +
                            "In OneCrew nicht (mehr) aktiv in dieser Filiale.";
            }

            if (!matched.ContainsKey(hit.Id)) matched[hit.Id] = m;
            gematchtZeilen.Add(m);
        }

        // ── Fehlende = aktive Lohn-MA ohne Mirus-Zeile ──
        var fehlend = kandidaten
            .Where(e => !matched.ContainsKey(e.Id))
            .OrderBy(e => e.FirstName ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.LastName ?? "", StringComparer.OrdinalIgnoreCase)
            .ToList();

        var details = new List<MaDetail>();
        if (fehlend.Count > 0)
        {
            var ids = fehlend.Select(e => e.Id).ToList();

            var banken = await _db.EmployeeBankAccounts.AsNoTracking()
                .Where(b => ids.Contains(b.EmployeeId) && b.ValidFrom <= today && (b.ValidTo == null || b.ValidTo >= today))
                .ToListAsync(ct);
            var qsts = await _db.EmployeeQuellensteuer.AsNoTracking()
                .Where(q => ids.Contains(q.EmployeeId) && q.ValidFrom <= today && (q.ValidTo == null || q.ValidTo >= today))
                .ToListAsync(ct);
            var familie = await _db.EmployeeFamilyMembers.AsNoTracking()
                .Where(f => ids.Contains(f.EmployeeId) && f.DateOfDeath == null)
                .ToListAsync(ct);
            var permits = await _db.EmployeePermitHistories.AsNoTracking()
                .Include(p => p.PermitType)
                .Where(p => ids.Contains(p.EmployeeId))
                .ToListAsync(ct);

            foreach (var e in fehlend)
            {
                var d = new MaDetail
                {
                    EmployeeId = e.Id,
                    Personalnummer = e.EmployeeNumber,
                    AlteNummern = e.NumberAliases.Select(a => a.Number).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList(),
                    Anrede = e.Salutation,
                    Geschlecht = GeschlechtText(e.Gender),
                    Vorname = e.FirstName,
                    Nachname = e.LastName,
                    Ledigname = e.MaidenName,
                    Geburtsdatum = e.DateOfBirth,
                    Ahv = FormatAhv(e.SocialSecurityNumber),
                    Zivilstand = ZivilstandText(e.MaritalStatus),
                    ZivilstandSeit = e.MaritalStatusSince,
                    Nationalitaet = !string.IsNullOrWhiteSpace(e.NationalityRef?.NameDe) ? e.NationalityRef!.NameDe
                                    : (e.NationalityRef?.Code ?? e.Nationality),
                    NationalitaetCode = e.NationalityRef?.Code,
                    Zemis = e.ZemisNumber,
                    Konfession = e.Religion,
                    Sprache = SpracheText(e.LanguageCode),
                    Heimatort = e.PlaceOfOrigin,
                    Strasse = e.Street,
                    Plz = e.ZipCode,
                    Ort = e.City,
                    Kanton = e.CantonCode,
                    Land = e.Country,
                    Telefon = e.PhoneMobile,
                    Telefon2 = e.Phone2,
                    Email = e.Email,
                    Eintritt = e.EntryDate,
                    LgavPflichtig = e.LgavPflichtig,
                    TeilzeitUnter8h = e.TeilzeitUnter8hWoche,
                };

                // Bewilligung: jüngster History-Eintrag, sonst Stammfeld
                var ph = permits.Where(p => p.EmployeeId == e.Id)
                    .OrderByDescending(p => p.ValidFrom).FirstOrDefault();
                if (ph != null)
                {
                    d.Bewilligung = ph.PermitType?.Code;
                    d.BewilligungText = ph.PermitType?.Description;
                    d.BewilligungBis = ph.ValidTo;
                }
                else if (e.PermitType != null)
                {
                    d.Bewilligung = e.PermitType.Code;
                    d.BewilligungText = e.PermitType.Description;
                }
                if (string.IsNullOrWhiteSpace(d.Bewilligung) && string.Equals(d.NationalitaetCode, "CH", StringComparison.OrdinalIgnoreCase))
                {
                    d.Bewilligung = "CH";
                    d.BewilligungText = "Schweizer/in — keine Bewilligung";
                }

                // Verträge dieser Filiale: laufende zuerst, dann die zwei jüngsten alten
                var vts = e.Employments.Where(em => em.CompanyProfileId == companyProfileId)
                    .OrderByDescending(em => em.ContractStartDate).ToList();
                var laufend = vts.Where(em => em.ContractEndDate == null || em.ContractEndDate.Value.Date >= todayDt).ToList();
                var alt = vts.Except(laufend).Take(laufend.Count == 0 ? 2 : 1).ToList();
                foreach (var em in laufend.Concat(alt))
                {
                    d.Vertraege.Add(new VertragInfo
                    {
                        Modell = em.EmploymentModel,
                        Lohnart = LohnartText(em.SalaryType, em.EmploymentModel),
                        Funktion = em.JobGroup?.Code,
                        FunktionText = em.JobTitle,
                        Vertragstyp = em.ContractType,
                        Von = em.ContractStartDate,
                        Bis = em.ContractEndDate,
                        PensumProzent = em.EmploymentPercentage,
                        Wochenstunden = em.WeeklyHours,
                        GarantierteStunden = em.GuaranteedHoursPerWeek,
                        Monatslohn = em.MonthlySalary,
                        MonatslohnFte = em.MonthlySalaryFte,
                        Stundenlohn = em.HourlyRate,
                        Ferienzahlung = em.VacationPaymentMode,
                        ProbezeitMonate = em.ProbationPeriodMonths,
                        ProbezeitBis = em.ProbationEndDate,
                        Aktiv = em.ContractEndDate == null || em.ContractEndDate.Value.Date >= todayDt,
                    });
                }
                var hauptVertrag = laufend.OrderByDescending(em => em.ContractStartDate).FirstOrDefault() ?? vts.FirstOrDefault();
                d.KostenstelleVorschlag = KostenstelleVorschlag(hauptVertrag);

                foreach (var b in banken.Where(b => b.EmployeeId == e.Id).OrderByDescending(b => b.IsHauptbank).ThenBy(b => b.Id))
                {
                    d.Banken.Add(new BankInfo
                    {
                        Iban = FormatIban(b.Iban),
                        Bank = b.BankName,
                        Kontoinhaber = b.Kontoinhaber,
                        Hauptbank = b.IsHauptbank,
                        Aufteilung = b.AufteilungTyp == "VOLL" ? null
                                   : $"{b.AufteilungTyp} {b.AufteilungWert:0.##}".Trim(),
                    });
                }

                var q = qsts.Where(x => x.EmployeeId == e.Id).OrderByDescending(x => x.ValidFrom).FirstOrDefault();
                if (q != null)
                {
                    d.Qst = new QstInfo
                    {
                        Pflichtig = true,
                        Tarif = q.TarifCode ?? q.QstCode,
                        TarifText = q.TarifBezeichnung,
                        Kanton = q.Steuerkanton,
                        Gemeinde = q.QstGemeinde,
                        GemeindeBfs = q.QstGemeindeBfsNr,
                        Kirchensteuer = q.Kirchensteuer,
                        Kinder = q.AnzahlKinder,
                        Prozent = q.Prozentsatz,
                        GueltigAb = q.ValidFrom,
                    };
                }
                else
                {
                    var hinweis = e.QuellensteuerBefreitAb != null
                        ? $"QST-befreit ab {e.QuellensteuerBefreitAb:dd.MM.yyyy}"
                        : (string.Equals(d.NationalitaetCode, "CH", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(d.Bewilligung, "C", StringComparison.OrdinalIgnoreCase)
                            ? "keine QST (CH / C-Bewilligung)"
                            : "keine QST-Erfassung in OneCrew — prüfen!");
                    d.Qst = new QstInfo { Pflichtig = false, Hinweis = hinweis };
                }

                foreach (var f in familie.Where(f => f.EmployeeId == e.Id)
                             .OrderBy(f => f.MemberType == "Kind" ? 1 : 0).ThenBy(f => f.DateOfBirth))
                {
                    d.Familie.Add(new FamilieInfo
                    {
                        Typ = f.MemberType,
                        Vorname = f.FirstName,
                        Nachname = f.LastName,
                        Geburtsdatum = f.DateOfBirth,
                        Ahv = FormatAhv(f.SocialSecurityNumber),
                        ImHaushalt = f.LebtImHaushalt,
                        InSchweiz = f.LivesInSwitzerland,
                    });
                }

                if (string.IsNullOrWhiteSpace(d.Personalnummer)) d.Luecken.Add("Personalnummer");
                if (string.IsNullOrWhiteSpace(d.Ahv)) d.Luecken.Add("AHV-Nummer");
                if (d.Geburtsdatum == null) d.Luecken.Add("Geburtsdatum");
                if (string.IsNullOrWhiteSpace(d.Strasse) || string.IsNullOrWhiteSpace(d.Plz) || string.IsNullOrWhiteSpace(d.Ort)) d.Luecken.Add("Adresse");
                if (string.IsNullOrWhiteSpace(d.Nationalitaet)) d.Luecken.Add("Nationalität");
                if (d.Banken.Count == 0) d.Luecken.Add("Bankverbindung (IBAN)");
                if (d.Vertraege.Count == 0 || !d.Vertraege.Any(v => v.Aktiv)) d.Luecken.Add("laufender Vertrag");
                if (d.Eintritt == null) d.Luecken.Add("Eintrittsdatum");

                details.Add(d);
            }
        }

        var res = new ErgebnisDto
        {
            Filiale = $"{branch.RestaurantCode} {branch.City ?? branch.BranchName}".Trim(),
            MirusZeilen = mirusRows.Count,
            OneCrewAktiv = kandidaten.Count,
            Gematcht = kandidaten.Count(e => matched.ContainsKey(e.Id)),
            Fehlend = details.Count,
            NurMirus = nurMirus.Count,
            MirusAusgetreten = ausgetreten.Count,
            OhneLohn = ohneLohn.Count,
            FehlendeMa = details,
            NurMirusZeilen = nurMirus,
            AusgetretenZeilen = ausgetreten,
            GematchtZeilen = gematchtZeilen,
        };
        return (null, res);
    }

    /// <summary>Bei mehreren Kandidaten: Name passt → der; sonst der aktivste.</summary>
    private static Employee Pick(List<Employee> cands, MirusZeile m)
    {
        if (cands.Count == 1) return cands[0];
        var nk = NameKey(m.Vorname, m.Nachname);
        var byName = cands.Where(c => NameKey(c.FirstName, c.LastName) == nk).ToList();
        if (byName.Count == 1) return byName[0];
        return cands.OrderByDescending(c => c.IsActive).ThenByDescending(c => c.Id).First();
    }

    // ─────────────────────────── Texte / Vorschläge ───────────────────────────

    private static string? KostenstelleVorschlag(Employment? em)
    {
        if (em == null) return null;
        if (em.JobGroup?.IsKader == true) return "Management";
        return (em.EmploymentModel ?? "").ToUpperInvariant() switch
        {
            "FIX-M" => "Management",
            "FLEX" or "UTP" => "Crew Flex",
            "FIX" or "MTP" => "Crew Fix",
            _ => null
        };
    }

    private static string? LohnartText(string? salaryType, string? modell)
    {
        var s = (salaryType ?? "").ToLowerInvariant();
        if (s.Contains("month") || s.Contains("monat")) return "Monatslohn";
        if (s.Contains("hour") || s.Contains("stund")) return "Stundenlohn";
        return (modell ?? "").ToUpperInvariant() switch
        {
            "FIX" or "FIX-M" => "Monatslohn",
            "FLEX" or "MTP" or "UTP" => "Stundenlohn",
            _ => salaryType
        };
    }

    private static string? GeschlechtText(string? g) => (g ?? "").ToLowerInvariant() switch
    {
        "male" or "m" => "männlich",
        "female" or "f" or "w" => "weiblich",
        "divers" or "d" => "divers",
        "" => null,
        _ => g
    };

    private static string? ZivilstandText(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var k = s.Trim().ToLowerInvariant();
        return k switch
        {
            "ledig" or "single" => "ledig",
            "verheiratet" or "married" => "verheiratet",
            "geschieden" or "divorced" => "geschieden",
            "verwitwet" or "widowed" => "verwitwet",
            "getrennt" or "separated" => "getrennt",
            "eingetragene partnerschaft" or "registered_partnership" or "partnerschaft" => "eingetragene Partnerschaft",
            "aufgelöste partnerschaft" or "dissolved_partnership" => "aufgelöste Partnerschaft",
            _ => s.Trim()
        };
    }

    private static string? SpracheText(string? c) => (c ?? "").ToLowerInvariant() switch
    {
        "de" => "Deutsch",
        "fr" => "Französisch",
        "it" => "Italienisch",
        "en" => "Englisch",
        "" => null,
        _ => c
    };

    private static string? FormatAhv(string? s)
    {
        var d = NormAhv(s);
        if (d.Length != 13) return string.IsNullOrWhiteSpace(s) ? null : s!.Trim();
        return $"{d[..3]}.{d.Substring(3, 4)}.{d.Substring(7, 4)}.{d.Substring(11, 2)}";
    }

    private static string? FormatIban(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var c = s.Replace(" ", "").ToUpperInvariant();
        return string.Join(" ", Enumerable.Range(0, (c.Length + 3) / 4).Select(i => c.Substring(i * 4, Math.Min(4, c.Length - i * 4))));
    }

    // ─────────────────────────── PDF ───────────────────────────

    private static string D(DateTime? d) => d == null ? "—" : d.Value.ToString("dd.MM.yyyy");
    private static string D(DateOnly? d) => d == null ? "—" : d.Value.ToString("dd.MM.yyyy");
    private static string S(string? s) => string.IsNullOrWhiteSpace(s) ? "—" : s!.Trim();
    private static string N(decimal? v, string suffix = "") => v == null ? "—" : v.Value.ToString("#,##0.00", CultureInfo.GetCultureInfo("de-CH")) + suffix;

    private static byte[] BuildPdf(ErgebnisDto res)
    {
        var created = DateTime.Now.ToString("dd.MM.yyyy HH:mm");

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(26);
                page.MarginBottom(26);
                page.MarginHorizontal(32);
                page.DefaultTextStyle(t => t.FontSize(9).FontColor("#222"));

                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Text("Mirus-Abgleich — in Mirus fehlende Mitarbeitende")
                            .SemiBold().FontSize(13).FontColor("#1a1a1a");
                        row.ConstantItem(200).AlignRight().Text($"{res.Filiale}  ·  {created}")
                            .FontSize(8.5f).FontColor("#555");
                    });
                    col.Item().PaddingTop(5).LineHorizontal(0.8f).LineColor("#ccc");
                });

                page.Footer().AlignCenter().DefaultTextStyle(x => x.FontSize(7.5f).FontColor("#777")).Text(t =>
                {
                    t.Span("Erfassungshilfe für Mirus — enthält Personendaten, nicht weitergeben  ·  Seite ");
                    t.CurrentPageNumber();
                    t.Span(" / ");
                    t.TotalPages();
                });

                page.Content().PaddingTop(8).Column(col =>
                {
                    col.Item().Text(
                        $"Aktive OneCrew-MA (mit Lohn): {res.OneCrewAktiv}   ·   in Mirus gefunden: {res.Gematcht}   ·   " +
                        $"fehlen in Mirus: {res.Fehlend}   ·   in Mirus ausgetreten, in OneCrew aktiv: {res.MirusAusgetreten}   ·   " +
                        $"nur in Mirus: {res.NurMirus}   ·   Mirus-Zeilen: {res.MirusZeilen}")
                        .FontSize(8.5f).FontColor("#444");

                    if (res.FehlendeMa.Count == 0)
                    {
                        col.Item().PaddingTop(14).Text("Alle aktiven OneCrew-Mitarbeitenden dieser Filiale sind in Mirus vorhanden.")
                            .FontSize(11).FontColor("#166534");
                    }
                    else
                    {
                        col.Item().PaddingTop(8).Text($"Fehlende Mitarbeitende ({res.FehlendeMa.Count}) — Übersicht")
                            .SemiBold().FontSize(11);
                        col.Item().PaddingTop(4).Table(t =>
                        {
                            t.ColumnsDefinition(c =>
                            {
                                c.ConstantColumn(60);
                                c.RelativeColumn(3);
                                c.ConstantColumn(62);
                                c.ConstantColumn(90);
                                c.ConstantColumn(62);
                                c.RelativeColumn(2);
                            });
                            t.Header(h =>
                            {
                                foreach (var s in new[] { "Pers. Nr.", "Name", "Geb.-Datum", "AHV-Nummer", "Eintritt", "Kostenstelle" })
                                    h.Cell().Background("#f3f3f3").Padding(3).Text(s).SemiBold().FontSize(8);
                            });
                            foreach (var m in res.FehlendeMa)
                            {
                                t.Cell().BorderBottom(0.3f).BorderColor("#eee").Padding(3).Text(S(m.Personalnummer)).FontSize(8.5f);
                                t.Cell().BorderBottom(0.3f).BorderColor("#eee").Padding(3).Text($"{m.Vorname} {m.Nachname}".Trim()).FontSize(8.5f);
                                t.Cell().BorderBottom(0.3f).BorderColor("#eee").Padding(3).Text(D(m.Geburtsdatum)).FontSize(8.5f);
                                t.Cell().BorderBottom(0.3f).BorderColor("#eee").Padding(3).Text(S(m.Ahv)).FontSize(8.5f);
                                t.Cell().BorderBottom(0.3f).BorderColor("#eee").Padding(3).Text(D(m.Eintritt)).FontSize(8.5f);
                                t.Cell().BorderBottom(0.3f).BorderColor("#eee").Padding(3).Text(S(m.KostenstelleVorschlag)).FontSize(8.5f);
                            }
                        });

                        foreach (var m in res.FehlendeMa)
                        {
                            col.Item().PageBreak();
                            RenderMa(col, m);
                        }
                    }

                    if (res.AusgetretenZeilen.Count > 0 || res.NurMirusZeilen.Count > 0)
                    {
                        col.Item().PageBreak();
                        col.Item().Text("Anhang — Mirus-Zeilen ohne aktives OneCrew-Gegenstück").SemiBold().FontSize(11);
                        col.Item().PaddingTop(2).Text("Nur zur Information: diese Personen sind in Mirus erfasst, aber in OneCrew nicht als aktiv in dieser Filiale geführt (Austritt, Übertritt in andere Filiale, oder nie in OneCrew).")
                            .FontSize(8).FontColor("#666");
                        RenderMirusTabelle(col, "In Mirus ausgetreten, in OneCrew aktiv (reaktivieren?)", res.AusgetretenZeilen);
                        RenderMirusTabelle(col, "Nur in Mirus", res.NurMirusZeilen);
                    }
                });
            });
        }).GeneratePdf();
    }

    private static void RenderMirusTabelle(ColumnDescriptor col, string titel, List<MirusZeile> rows)
    {
        if (rows.Count == 0) return;
        col.Item().PaddingTop(10).Text($"{titel} ({rows.Count})").SemiBold().FontSize(9.5f);
        col.Item().PaddingTop(3).Table(t =>
        {
            t.ColumnsDefinition(c =>
            {
                c.ConstantColumn(60);
                c.RelativeColumn(2);
                c.ConstantColumn(62);
                c.ConstantColumn(62);
                c.ConstantColumn(62);
                c.RelativeColumn(3);
            });
            t.Header(h =>
            {
                foreach (var s in new[] { "Pers. Nr.", "Name (Mirus)", "Geb.-Datum", "Eintritt", "Austritt", "Hinweis" })
                    h.Cell().Background("#f3f3f3").Padding(3).Text(s).SemiBold().FontSize(8);
            });
            foreach (var m in rows)
            {
                t.Cell().BorderBottom(0.3f).BorderColor("#eee").Padding(3).Text(S(m.Personalnummer)).FontSize(8);
                t.Cell().BorderBottom(0.3f).BorderColor("#eee").Padding(3).Text($"{m.Vorname} {m.Nachname}".Trim()).FontSize(8);
                t.Cell().BorderBottom(0.3f).BorderColor("#eee").Padding(3).Text(D(m.Geburtsdatum)).FontSize(8);
                t.Cell().BorderBottom(0.3f).BorderColor("#eee").Padding(3).Text(D(m.Eintritt)).FontSize(8);
                t.Cell().BorderBottom(0.3f).BorderColor("#eee").Padding(3).Text(D(m.Austritt)).FontSize(8);
                t.Cell().BorderBottom(0.3f).BorderColor("#eee").Padding(3).Text(S(m.Hinweis)).FontSize(7.5f).FontColor("#555");
            }
        });
    }

    private static void RenderMa(ColumnDescriptor col, MaDetail m)
    {
        col.Item().Row(row =>
        {
            row.RelativeItem().Text($"{m.Vorname} {m.Nachname}".Trim()).SemiBold().FontSize(14).FontColor("#1a1a1a");
            row.ConstantItem(180).AlignRight().Text($"Pers. Nr. {S(m.Personalnummer)}").SemiBold().FontSize(11);
        });
        if (m.Luecken.Count > 0)
            col.Item().PaddingTop(3).Background("#fef2f2").Padding(5)
                .Text("In OneCrew noch unvollständig: " + string.Join(", ", m.Luecken))
                .FontSize(8.5f).FontColor("#991b1b");

        Sektion(col, "Personalien", new (string, string)[]
        {
            ("Anrede", S(m.Anrede)),                       ("Geschlecht", S(m.Geschlecht)),
            ("Nachname", S(m.Nachname)),                   ("Vorname", S(m.Vorname)),
            ("Ledigname", S(m.Ledigname)),                 ("Geburtsdatum", D(m.Geburtsdatum)),
            ("AHV-Nummer", S(m.Ahv)),                      ("Zivilstand", S(m.Zivilstand) + (m.ZivilstandSeit != null ? $" (seit {D(m.ZivilstandSeit)})" : "")),
            ("Nationalität", S(m.Nationalitaet) + (m.NationalitaetCode != null ? $" ({m.NationalitaetCode})" : "")),
                                                          ("Bewilligung", S(m.Bewilligung) + (m.BewilligungText != null ? $" — {m.BewilligungText}" : "") + (m.BewilligungBis != null ? $", gültig bis {D(m.BewilligungBis)}" : "")),
            ("ZEMIS-Nr.", S(m.Zemis)),                     ("Konfession", S(m.Konfession)),
            ("Sprache", S(m.Sprache)),                     ("Heimatort", S(m.Heimatort)),
            ("Alte Pers. Nr.", m.AlteNummern.Count == 0 ? "—" : string.Join(", ", m.AlteNummern)), ("", ""),
        });

        Sektion(col, "Adresse & Kontakt", new (string, string)[]
        {
            ("Strasse", S(m.Strasse)),                     ("PLZ / Ort", $"{S(m.Plz)} {S(m.Ort)}".Replace("— —", "—")),
            ("Kanton", S(m.Kanton)),                       ("Land", S(m.Land)),
            ("Telefon", S(m.Telefon)),                     ("Telefon 2", S(m.Telefon2)),
            ("E-Mail", S(m.Email)),                        ("", ""),
        });

        Sektion(col, "Anstellung", new (string, string)[]
        {
            ("Eintritt", D(m.Eintritt)),                   ("Kostenstelle (Vorschlag)", S(m.KostenstelleVorschlag)),
            ("L-GAV-pflichtig", m.LgavPflichtig ? "ja" : "nein"), ("NBU", m.TeilzeitUnter8h ? "nein (< 8 h/Woche)" : "ja"),
        });

        col.Item().PaddingTop(8).Text("Vertrag / Verträge").SemiBold().FontSize(9.5f).FontColor("#3f3f3f");
        if (m.Vertraege.Count == 0)
            col.Item().PaddingTop(2).Text("Kein Vertrag in dieser Filiale erfasst.").FontSize(8.5f).FontColor("#991b1b");
        foreach (var v in m.Vertraege)
        {
            var titel = $"{S(v.Modell)} · {S(v.Lohnart)} · {S(v.Funktion)}" + (v.FunktionText != null ? $" ({v.FunktionText})" : "") + (v.Aktiv ? "" : "   [abgelaufen]");
            col.Item().PaddingTop(3).Text(titel).SemiBold().FontSize(8.5f).FontColor(v.Aktiv ? "#1a1a1a" : "#888");
            var zeilen = new List<(string, string)>
            {
                ("Vertragsbeginn", D(v.Von)),                 ("Vertragsende", v.Bis == null ? "unbefristet" : D(v.Bis)),
            };
            var modell = (v.Modell ?? "").ToUpperInvariant();
            if (modell is "FIX" or "FIX-M")
            {
                zeilen.Add(("Pensum", v.PensumProzent == null ? "—" : $"{v.PensumProzent:0.##} %"));
                zeilen.Add(("Monatslohn", N(v.Monatslohn, " CHF")));
                if (v.MonatslohnFte != null) { zeilen.Add(("Monatslohn 100 %", N(v.MonatslohnFte, " CHF"))); zeilen.Add(("", "")); }
            }
            else
            {
                zeilen.Add(("Stundenlohn", N(v.Stundenlohn, " CHF")));
                zeilen.Add(("Wochenstunden", v.Wochenstunden == null ? "—" : $"{v.Wochenstunden:0.##} h"));
                if (v.GarantierteStunden != null) { zeilen.Add(("Garantierte Std./Woche", $"{v.GarantierteStunden:0.##} h")); zeilen.Add(("", "")); }
            }
            zeilen.Add(("Ferienzahlung", S(v.Ferienzahlung)));
            zeilen.Add(("Probezeit", v.ProbezeitMonate == null ? "—" : $"{v.ProbezeitMonate} Mt." + (v.ProbezeitBis != null ? $" (bis {D(v.ProbezeitBis)})" : "")));
            if (!string.IsNullOrWhiteSpace(v.Vertragstyp)) { zeilen.Add(("Vertragstyp", v.Vertragstyp!)); zeilen.Add(("", "")); }
            KvTabelle(col, zeilen);
        }

        col.Item().PaddingTop(8).Text("Bankverbindung").SemiBold().FontSize(9.5f).FontColor("#3f3f3f");
        if (m.Banken.Count == 0)
            col.Item().PaddingTop(2).Text("Keine Bankverbindung in OneCrew erfasst.").FontSize(8.5f).FontColor("#991b1b");
        foreach (var b in m.Banken)
        {
            KvTabelle(col, new List<(string, string)>
            {
                ("IBAN", S(b.Iban) + (b.Hauptbank ? "" : "  (Nebenkonto)")), ("Bank", S(b.Bank)),
                ("Kontoinhaber", S(b.Kontoinhaber)), ("Aufteilung", b.Aufteilung ?? "voll"),
            });
        }

        col.Item().PaddingTop(8).Text("Quellensteuer").SemiBold().FontSize(9.5f).FontColor("#3f3f3f");
        if (m.Qst == null || !m.Qst.Pflichtig)
            col.Item().PaddingTop(2).Text(S(m.Qst?.Hinweis)).FontSize(8.5f)
                .FontColor((m.Qst?.Hinweis ?? "").Contains("prüfen") ? "#991b1b" : "#444");
        else
        {
            var q = m.Qst;
            KvTabelle(col, new List<(string, string)>
            {
                ("Tarif", S(q.Tarif) + (q.TarifText != null ? $" — {q.TarifText}" : "")), ("Kanton", S(q.Kanton)),
                ("Gemeinde", S(q.Gemeinde) + (q.GemeindeBfs != null ? $" (BFS {q.GemeindeBfs})" : "")), ("Kirchensteuer", q.Kirchensteuer ? "ja" : "nein"),
                ("Kinder (QST)", q.Kinder?.ToString() ?? "—"), ("Gültig ab", D(q.GueltigAb)),
                ("Satz", q.Prozent == null ? "—" : $"{q.Prozent:0.##} %"), ("", ""),
            });
        }

        col.Item().PaddingTop(8).Text("Familie (Ehepartner / Kinder — Familienzulagen, QST)").SemiBold().FontSize(9.5f).FontColor("#3f3f3f");
        if (m.Familie.Count == 0)
            col.Item().PaddingTop(2).Text("Keine Familienmitglieder erfasst.").FontSize(8.5f).FontColor("#666");
        else
        {
            col.Item().PaddingTop(3).Table(t =>
            {
                t.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(95);
                    c.RelativeColumn();
                    c.ConstantColumn(66);
                    c.ConstantColumn(95);
                    c.ConstantColumn(60);
                    c.ConstantColumn(60);
                });
                t.Header(h =>
                {
                    foreach (var s in new[] { "Typ", "Name", "Geb.-Datum", "AHV-Nummer", "Im Haushalt", "In der CH" })
                        h.Cell().Background("#f3f3f3").Padding(3).Text(s).SemiBold().FontSize(8);
                });
                foreach (var f in m.Familie)
                {
                    t.Cell().BorderBottom(0.3f).BorderColor("#eee").Padding(3).Text(S(f.Typ)).FontSize(8.5f);
                    t.Cell().BorderBottom(0.3f).BorderColor("#eee").Padding(3).Text($"{f.Vorname} {f.Nachname}".Trim()).FontSize(8.5f);
                    t.Cell().BorderBottom(0.3f).BorderColor("#eee").Padding(3).Text(D(f.Geburtsdatum)).FontSize(8.5f);
                    t.Cell().BorderBottom(0.3f).BorderColor("#eee").Padding(3).Text(S(f.Ahv)).FontSize(8.5f);
                    t.Cell().BorderBottom(0.3f).BorderColor("#eee").Padding(3).Text(f.ImHaushalt ? "ja" : "nein").FontSize(8.5f);
                    t.Cell().BorderBottom(0.3f).BorderColor("#eee").Padding(3).Text(f.InSchweiz ? "ja" : "nein").FontSize(8.5f);
                }
            });
        }
    }

    private static void Sektion(ColumnDescriptor col, string titel, (string, string)[] paare)
    {
        col.Item().PaddingTop(8).Text(titel).SemiBold().FontSize(9.5f).FontColor("#3f3f3f");
        KvTabelle(col, paare.ToList());
    }

    /// <summary>Zweispaltige Label/Wert-Tabelle (2 Paare pro Zeile). Breiten: 2×(95+~170) = passt in 531 pt.</summary>
    private static void KvTabelle(ColumnDescriptor col, List<(string Label, string Wert)> paare)
    {
        col.Item().PaddingTop(2).Table(t =>
        {
            t.ColumnsDefinition(c =>
            {
                c.ConstantColumn(95);
                c.RelativeColumn();
                c.ConstantColumn(95);
                c.RelativeColumn();
            });
            foreach (var (label, wert) in paare)
            {
                if (label.Length == 0)
                {
                    t.Cell().Padding(2).Text(" ");
                    t.Cell().Padding(2).Text(" ");
                    continue;
                }
                t.Cell().BorderBottom(0.3f).BorderColor("#eee").Padding(2).Text(label).FontSize(8).FontColor("#666");
                t.Cell().BorderBottom(0.3f).BorderColor("#eee").Padding(2).Text(wert).FontSize(8.5f);
            }
        });
    }

    // ─────────────────────────── Parsing ───────────────────────────

    private static List<MirusZeile> ParseMirusExport(Stream stream, string fileName)
    {
        IWorkbook wb = fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
            ? new XSSFWorkbook(stream)
            : new HSSFWorkbook(stream);
        var sheet = wb.GetSheetAt(0) ?? throw new InvalidOperationException("Leeres Arbeitsblatt.");

        int headerRow = -1;
        var col = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int r = 0; r <= Math.Min(sheet.LastRowNum, 15); r++)
        {
            var row = sheet.GetRow(r);
            if (row == null) continue;
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int c = 0; c < row.LastCellNum; c++)
            {
                var h = CellStr(row.GetCell(c));
                if (!string.IsNullOrWhiteSpace(h)) map[CollapseWs(h)] = c;
            }
            bool hasPers = map.Keys.Any(k => k.StartsWith("Pers", StringComparison.OrdinalIgnoreCase)
                                          || k.Contains("Personal", StringComparison.OrdinalIgnoreCase));
            bool hasName = map.Keys.Any(k => k.Equals("Name", StringComparison.OrdinalIgnoreCase)
                                          || k.Equals("Nachname", StringComparison.OrdinalIgnoreCase));
            bool hasVor = map.Keys.Any(k => k.Equals("Vorname", StringComparison.OrdinalIgnoreCase));
            if (hasPers && hasName && hasVor)
            {
                headerRow = r;
                col = map;
                break;
            }
        }
        if (headerRow < 0)
            throw new InvalidOperationException("Kopfzeile mit «Personal-Nr.», «Name» und «Vorname» nicht gefunden.");

        int Col(params string[] names)
        {
            foreach (var n in names)
            {
                if (col.TryGetValue(n, out var i)) return i;
            }
            foreach (var n in names)
            {
                var soft = col.FirstOrDefault(kv => kv.Key.Contains(n, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(soft.Key)) return soft.Value;
            }
            return -1;
        }

        // «Mirus - PNR» darf NICHT als Personalnummer gegriffen werden → exakte Namen zuerst.
        int cNr = Col("Personal-Nr.:", "Personal-Nr.", "Personal-Nr", "Personalnummer", "Pers. Nr.", "Pers.-Nr.", "Pers Nr", "Pers.Nr.");
        int cPnr = Col("Mirus - PNR", "Mirus-PNR", "PNR");
        if (cPnr == cNr) cPnr = -1;
        int cName = Col("Name", "Nachname");
        int cVor = Col("Vorname");
        int cGeb = Col("Geb.-Datum", "Geburtsdatum", "Geb.Datum", "Geb-Datum", "Geburtstag");
        int cAhv = Col("AHV-Nummer", "AHV-Nr.", "AHV-Nr", "AHV", "Sozialversicherungsnummer");
        int cEin = Col("Eintritt", "Eintrittsdatum");
        int cAus = Col("Austritt", "Austrittsdatum");
        int cKst = Col("Kostenstelle", "Kst");

        if (cNr < 0) throw new InvalidOperationException("Spalte «Personal-Nr.» fehlt.");

        var list = new List<MirusZeile>();
        for (int r = headerRow + 1; r <= sheet.LastRowNum; r++)
        {
            var row = sheet.GetRow(r);
            if (row == null) continue;
            var nr = CellStr(row.GetCell(cNr));
            var last = cName >= 0 ? CellStr(row.GetCell(cName)) : "";
            var first = cVor >= 0 ? CellStr(row.GetCell(cVor)) : "";
            if (string.IsNullOrWhiteSpace(nr) && string.IsNullOrWhiteSpace(last) && string.IsNullOrWhiteSpace(first)) continue;
            if (string.IsNullOrWhiteSpace(last) && string.IsNullOrWhiteSpace(first)) continue; // Total-/Leerzeilen

            list.Add(new MirusZeile
            {
                Zeile = r + 1,
                Personalnummer = NullIfBlank(nr),
                MirusPnr = cPnr >= 0 ? NullIfBlank(CellStr(row.GetCell(cPnr))) : null,
                Nachname = CollapseWs(last),
                Vorname = CollapseWs(first),
                Geburtsdatum = cGeb >= 0 ? CellDate(row.GetCell(cGeb)) : null,
                Ahv = cAhv >= 0 ? NullIfBlank(CellStr(row.GetCell(cAhv))) : null,
                Eintritt = cEin >= 0 ? CellDate(row.GetCell(cEin)) : null,
                Austritt = cAus >= 0 ? CellDate(row.GetCell(cAus)) : null,
                Kostenstelle = cKst >= 0 ? NullIfBlank(CellStr(row.GetCell(cKst))) : null,
            });
        }
        return list;
    }

    private static string CellStr(ICell? cell)
    {
        if (cell == null) return "";
        return cell.CellType switch
        {
            CellType.Numeric => DateUtil.IsCellDateFormatted(cell) && cell.DateCellValue != null
                ? cell.DateCellValue.Value.ToString("dd.MM.yyyy")
                : cell.NumericCellValue.ToString("0.##########", CultureInfo.InvariantCulture),
            CellType.Boolean => cell.BooleanCellValue ? "true" : "false",
            CellType.Formula => cell.ToString() ?? "",
            _ => cell.ToString() ?? ""
        };
    }

    private static DateTime? CellDate(ICell? cell)
    {
        if (cell == null) return null;
        try
        {
            if (cell.CellType == CellType.Numeric)
            {
                if (DateUtil.IsCellDateFormatted(cell) && cell.DateCellValue != null) return cell.DateCellValue.Value.Date;
                var v = cell.NumericCellValue;
                if (v > 20000 && v < 80000) return DateUtil.GetJavaDate(v).Date; // Excel-Seriendatum
            }
        }
        catch { /* Fallback auf Text */ }
        var s = (cell.ToString() ?? "").Trim();
        if (s.Length == 0) return null;
        var formats = new[] { "dd.MM.yyyy", "d.M.yyyy", "dd.MM.yy", "yyyy-MM-dd", "dd/MM/yyyy" };
        if (DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d.Date;
        return null;
    }

    private static string NormNumber(string? s)
    {
        var t = (s ?? "").Trim().ToLowerInvariant().Replace(" ", "");
        // «1220001.0» aus numerischen Zellen → «1220001»
        if (t.EndsWith(".0")) t = t[..^2];
        return t;
    }

    private static string NormAhv(string? s) => new string((s ?? "").Where(char.IsDigit).ToArray());

    private static string NameKey(string? first, string? last)
        => (NormName(first) + "|" + NormName(last)).Trim('|');

    private static string NormName(string? s)
    {
        var t = CollapseWs(s).ToLowerInvariant();
        t = t.Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("ß", "ss");
        var norm = t.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder();
        foreach (var ch in norm)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        }
        return sb.ToString().Replace("-", " ");
    }

    private static string CollapseWs(string? s) => Regex.Replace((s ?? "").Trim(), @"\s+", " ");
    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s!.Trim();
}
