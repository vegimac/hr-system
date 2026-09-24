using System.Security.Claims;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using HrSystem.Services.WebStamp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Briefpost über den Webservice WebStamp der Schweizerischen Post
/// (Walter 24.09.2026, System → Kommunikation → «Briefpost (WebStamp)»).
///
/// OneCrew erstellt den Brief als PDF (Adresse im Sichtfenster), die Post
/// frankiert, druckt, couvertiert und verschickt ihn (Druck- und Versandservice:
/// <c>printservice = true</c> + <c>document</c>). Kosten laut Post: Porto +
/// CHF 0.20 pro Seite + CHF 0.20 pro Brief.
///
/// TESTPHASE: Bestellungen nur gegen die Testumgebung der Post
/// (<see cref="WebStampEndpunkte.BestellungErlaubt"/>). Die kostenlose Vorschau
/// geht überall. Nur admin — der Versand geschieht im Namen der Firma und das
/// WSWS-Passwort ist sensibel (gleiche Grenze wie eCall/SMTP).
/// </summary>
[Authorize(Roles = "admin")]
[ApiController]
[Route("api/webstamp")]
public class WebStampController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly SimpleAesService _aes;
    private readonly WebStampClient _client;
    private readonly WebStampBriefPdfService _pdf;
    private readonly ILogger<WebStampController> _log;

    public WebStampController(AppDbContext db, SimpleAesService aes, WebStampClient client,
                              WebStampBriefPdfService pdf, ILogger<WebStampController> log)
    {
        _db = db;
        _aes = aes;
        _client = client;
        _pdf = pdf;
        _log = log;
    }

    // ── Einstellungen ────────────────────────────────────────────────────────

    public class EinstellungenDto
    {
        public string? Umgebung { get; set; }
        public string? ApplicationId { get; set; }
        public string? KundenId { get; set; }
        public string? Password { get; set; }       // PUT: leer = unverändert
        public int? ProduktNummer { get; set; }
        public bool FensterRechts { get; set; }
    }

    [HttpGet("einstellungen")]
    public async Task<IActionResult> GetEinstellungen()
    {
        var s = await _db.WebStampSettings.AsNoTracking().FirstOrDefaultAsync(r => r.Id == 1);
        var umgebung = s?.Umgebung ?? "test";
        return Ok(new
        {
            umgebung,
            applicationId = s?.ApplicationId ?? "",
            kundenId = s?.KundenId ?? "",
            hasPassword = !string.IsNullOrEmpty(s?.PasswordEncrypted),
            produktNummer = s?.ProduktNummer,
            fensterRechts = s?.FensterRechts ?? false,
            url = WebStampEndpunkte.Url(umgebung),
            bestellungErlaubt = WebStampEndpunkte.BestellungErlaubt(umgebung),
        });
    }

    [HttpPut("einstellungen")]
    public async Task<IActionResult> PutEinstellungen([FromBody] EinstellungenDto dto)
    {
        var appId = (dto.ApplicationId ?? "").Trim();
        var kundenId = (dto.KundenId ?? "").Trim();
        if (appId.Length > 32)
            return BadRequest(new { error = "APPLICATION_ID", message = "Die Application-ID hat höchstens 32 Zeichen." });
        if (kundenId.Length > 10)
            return BadRequest(new { error = "KUNDEN_ID", message = "Die WS-Kunden-ID hat höchstens 10 Zeichen." });

        var s = await _db.WebStampSettings.FirstOrDefaultAsync(r => r.Id == 1);
        if (s == null)
        {
            s = new WebStampSetting { Id = 1 };
            _db.WebStampSettings.Add(s);
        }
        s.Umgebung = WebStampEndpunkte.IstProd(dto.Umgebung) ? "prod" : "test";
        s.ApplicationId = appId.Length == 0 ? null : appId;
        s.KundenId = kundenId.Length == 0 ? null : kundenId;
        if (!string.IsNullOrEmpty(dto.Password))
            s.PasswordEncrypted = _aes.Encrypt(dto.Password);
        s.ProduktNummer = dto.ProduktNummer;
        s.FensterRechts = dto.FensterRechts;
        s.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        _log.LogInformation("[WebStamp] Einstellungen gespeichert (umgebung={Umgebung} kunde={Kunde})",
                            s.Umgebung, s.KundenId ?? "<leer>");
        return Ok(new { ok = true });
    }

    private async Task<(WebStampSetting? S, WebStampSoap.Zugang? Z, IActionResult? Fehler)> ZugangAsync(bool mitLogin = true)
    {
        var s = await _db.WebStampSettings.AsNoTracking().FirstOrDefaultAsync(r => r.Id == 1);
        if (string.IsNullOrWhiteSpace(s?.ApplicationId))
            return (s, null, BadRequest(new { error = "KEINE_APPLICATION_ID",
                message = "Zuerst die Application-ID der Post eintragen und speichern." }));
        if (mitLogin && (string.IsNullOrWhiteSpace(s.KundenId) || string.IsNullOrEmpty(s.PasswordEncrypted)))
            return (s, null, BadRequest(new { error = "KEIN_LOGIN",
                message = "Zuerst WS-Kunden-ID und WSWS-Passwort eintragen und speichern." }));
        var pw = string.IsNullOrEmpty(s.PasswordEncrypted) ? null : _aes.Decrypt(s.PasswordEncrypted);
        return (s, new WebStampSoap.Zugang(s.ApplicationId!, s.KundenId, pw), null);
    }

    private static object Fehlerbild(WebStampClient.Antwort a) => new
    {
        ok = false,
        httpStatus = a.HttpStatus,
        dauerMs = a.DauerMs,
        fehler = a.Fehler,
        fehlerNummer = a.Fault?.FehlerNummer,
        requestId = a.Fault?.RequestId,
    };

    // ── Verbindung ───────────────────────────────────────────────────────────

    /// <summary>Ping braucht keine Zugangsdaten — zeigt nur, dass die Post erreichbar ist.</summary>
    [HttpPost("ping")]
    public async Task<IActionResult> Ping(CancellationToken ct)
    {
        var s = await _db.WebStampSettings.AsNoTracking().FirstOrDefaultAsync(r => r.Id == 1);
        var url = WebStampEndpunkte.Url(s?.Umgebung);
        var a = await _client.RufeAsync(url, "ping", WebStampSoap.Ping(), ct);
        if (!a.Ok) return Ok(Fehlerbild(a));
        return Ok(new { ok = true, url, dauerMs = a.DauerMs,
                        zeit = a.Resultat!.Element("date")?.Value,
                        requestId = a.Resultat.Element("request_id")?.Value });
    }

    /// <summary>Prüft den Login: Verrechnungsart, Frankierlizenz, Produktsortimente.</summary>
    [HttpPost("kunde")]
    public async Task<IActionResult> Kunde(CancellationToken ct)
    {
        var (s, z, fehler) = await ZugangAsync();
        if (fehler != null) return fehler;
        var a = await _client.RufeAsync(WebStampEndpunkte.Url(s!.Umgebung), "get_customer_data",
                                        WebStampSoap.KundenDaten(z!), ct);
        if (!a.Ok) return Ok(Fehlerbild(a));
        var k = WebStampSoap.LiesKunde(a.Resultat!);
        return Ok(new { ok = true, dauerMs = a.DauerMs, lizenz = k.Lizenz, zahlungsart = k.Zahlungsart,
                        sortimente = k.Sortimente });
    }

    [HttpGet("produkte")]
    public async Task<IActionResult> Produkte(CancellationToken ct)
    {
        var (s, z, fehler) = await ZugangAsync();
        if (fehler != null) return fehler;
        var a = await _client.RufeAsync(WebStampEndpunkte.Url(s!.Umgebung), "get_products",
                                        WebStampSoap.Produkte(z!), ct);
        if (!a.Ok) return Ok(Fehlerbild(a));
        var liste = WebStampSoap.LiesProdukte(a.Resultat!)
            .OrderBy(p => p.Zone == 3 ? 0 : 1).ThenBy(p => p.Barcode).ThenBy(p => p.Kategorie).ThenBy(p => p.Preis)
            .ToList();
        return Ok(new { ok = true, produkte = liste });
    }

    // ── Empfänger + Absender ─────────────────────────────────────────────────

    [HttpGet("mitarbeiter")]
    public async Task<IActionResult> Mitarbeiter([FromQuery] string? q)
    {
        q = (q ?? "").Trim();
        if (q.Length < 2) return Ok(Array.Empty<object>());
        var muster = $"%{q}%";
        var treffer = await _db.Employees.AsNoTracking()
            .Where(e => EF.Functions.ILike(e.LastName, muster) || EF.Functions.ILike(e.FirstName, muster)
                     || EF.Functions.ILike(e.EmployeeNumber, muster)
                     || EF.Functions.ILike(e.FirstName + " " + e.LastName, muster))
            .OrderBy(e => e.LastName).ThenBy(e => e.FirstName)
            .Take(20)
            .ToListAsync();
        return Ok(treffer.Select(e => new
        {
            id = e.Id,
            nummer = e.EmployeeNumber,
            name = $"{e.FirstName} {e.LastName}".Trim(),
            adresse = AdressZeilen(e),
            adresseVollstaendig = AdresseVollstaendig(e),
        }));
    }

    [HttpGet("filialen")]
    public async Task<IActionResult> Filialen()
    {
        var liste = await _db.CompanyProfiles.AsNoTracking().OrderBy(c => c.CompanyName).ThenBy(c => c.BranchName).ToListAsync();
        return Ok(liste.Select(c => new { id = c.Id, name = FilialName(c), zeilen = AbsenderZeilen(c) }));
    }

    public static List<string> AdressZeilen(Employee e)
    {
        var z = new List<string>();
        var anrede = (e.Salutation ?? "").Trim();
        if (anrede.Length is > 0 and <= 20) z.Add(anrede);
        z.Add($"{e.FirstName} {e.LastName}".Trim());
        if (!string.IsNullOrWhiteSpace(e.Street)) z.Add(e.Street.Trim());
        var plzOrt = $"{e.ZipCode} {e.City}".Trim();
        if (plzOrt.Length > 0) z.Add(plzOrt);
        var land = (e.Country ?? "").Trim();
        if (land.Length > 0 && !IstSchweiz(land)) z.Add(land.ToUpperInvariant());
        return z;
    }

    public static bool AdresseVollstaendig(Employee e)
        => !string.IsNullOrWhiteSpace(e.Street) && !string.IsNullOrWhiteSpace(e.ZipCode)
        && !string.IsNullOrWhiteSpace(e.City);

    private static bool IstSchweiz(string land)
        => land.Equals("CH", StringComparison.OrdinalIgnoreCase)
        || land.Equals("CHE", StringComparison.OrdinalIgnoreCase)
        || land.StartsWith("Schweiz", StringComparison.OrdinalIgnoreCase)
        || land.StartsWith("Switzerland", StringComparison.OrdinalIgnoreCase);

    private static string FilialName(CompanyProfile c)
        => string.IsNullOrWhiteSpace(c.BranchName) ? c.CompanyName : $"{c.CompanyName} · {c.BranchName}";

    private static List<string> AbsenderZeilen(CompanyProfile c)
    {
        var z = new List<string> { c.CompanyName };
        if (!string.IsNullOrWhiteSpace(c.BranchName)) z.Add(c.BranchName!.Trim());
        var strasse = $"{c.Street} {c.HouseNumber}".Trim();
        if (strasse.Length > 0) z.Add(strasse);
        var plzOrt = $"{c.ZipCode} {c.City}".Trim();
        if (plzOrt.Length > 0) z.Add(plzOrt);
        return z.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
    }

    // ── Brief ────────────────────────────────────────────────────────────────

    public class BriefDto
    {
        public int? EmployeeId { get; set; }
        /// <summary>Freie Adresse (eine Zeile pro Adresszeile) — nur wenn kein MA gewählt ist.</summary>
        public string? Adresse { get; set; }
        public int? FilialeId { get; set; }
        public string? Betreff { get; set; }
        public string? Text { get; set; }
        public string? AbsenderName { get; set; }
        public int? ProduktNummer { get; set; }
        public bool? FensterRechts { get; set; }
    }

    private record Brief(byte[] Pdf, List<string> Empfaenger, int? EmployeeId, string Betreff);

    private async Task<(Brief? B, IActionResult? Fehler)> BaueBriefAsync(BriefDto dto, bool fensterRechtsStandard)
    {
        var betreff = (dto.Betreff ?? "").Trim();
        if (betreff.Length == 0)
            return (null, BadRequest(new { error = "BETREFF", message = "Bitte einen Betreff eingeben." }));

        List<string> empfaenger;
        CompanyProfile? filiale = null;
        if (dto.EmployeeId is int empId)
        {
            var e = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == empId);
            if (e == null) return (null, NotFound(new { error = "MA_UNBEKANNT", message = "Mitarbeiter nicht gefunden." }));
            if (!AdresseVollstaendig(e))
                return (null, BadRequest(new { error = "ADRESSE_UNVOLLSTAENDIG",
                    message = $"Bei {e.FirstName} {e.LastName} fehlen Strasse, PLZ oder Ort." }));
            empfaenger = AdressZeilen(e);
            if (dto.FilialeId == null)
            {
                // Absender = Filiale der jüngsten Anstellung.
                var cpId = await _db.Employments.AsNoTracking()
                    .Where(x => x.EmployeeId == empId && x.CompanyProfileId != null)
                    .OrderByDescending(x => x.ContractStartDate)
                    .Select(x => x.CompanyProfileId).FirstOrDefaultAsync();
                if (cpId != null) filiale = await _db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cpId);
            }
        }
        else
        {
            empfaenger = (dto.Adresse ?? "").Replace("\r\n", "\n").Split('\n')
                .Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            if (empfaenger.Count < 3)
                return (null, BadRequest(new { error = "ADRESSE", message =
                    "Bitte einen Mitarbeiter wählen oder eine Adresse mit mindestens 3 Zeilen eingeben (Name, Strasse, PLZ Ort)." }));
        }
        if (dto.FilialeId is int fId)
            filiale = await _db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(c => c.Id == fId);

        var pdf = _pdf.Generate(new WebStampBriefPdfService.BriefDaten(
            filiale != null ? AbsenderZeilen(filiale) : new List<string>(),
            empfaenger,
            filiale?.City,
            DateOnly.FromDateTime(DateTime.Now),
            betreff,
            dto.Text ?? "",
            dto.AbsenderName,
            dto.FensterRechts ?? fensterRechtsStandard));
        return (new Brief(pdf, empfaenger, dto.EmployeeId, betreff), null);
    }

    /// <summary>Nur das PDF — zum Anschauen, ohne die Post zu fragen.</summary>
    [HttpPost("brief/pdf")]
    public async Task<IActionResult> BriefPdf([FromBody] BriefDto dto)
    {
        var s = await _db.WebStampSettings.AsNoTracking().FirstOrDefaultAsync(r => r.Id == 1);
        var (b, fehler) = await BaueBriefAsync(dto, s?.FensterRechts ?? false);
        if (fehler != null) return fehler;
        return File(b!.Pdf, "application/pdf", "Brief.pdf");
    }

    /// <summary>Kostenlose Vorschau: Preis, erkannte Adresse/Fenster pro Sendung, Mitteilungen der Post.</summary>
    [HttpPost("brief/vorschau")]
    public Task<IActionResult> BriefVorschau([FromBody] BriefDto dto, CancellationToken ct)
        => BriefAnPostAsync(dto, bestellen: false, ct);

    /// <summary>Bestellung: die Post druckt und verschickt (Testphase: nur Testumgebung).</summary>
    [HttpPost("brief/senden")]
    public Task<IActionResult> BriefSenden([FromBody] BriefDto dto, CancellationToken ct)
        => BriefAnPostAsync(dto, bestellen: true, ct);

    private async Task<IActionResult> BriefAnPostAsync(BriefDto dto, bool bestellen, CancellationToken ct)
    {
        var (s, z, fehler) = await ZugangAsync();
        if (fehler != null) return fehler;
        if (bestellen && !WebStampEndpunkte.BestellungErlaubt(s!.Umgebung))
            return StatusCode(403, new { error = "NUR_TEST", message =
                "Testphase: Briefe werden nur über die Testumgebung der Post bestellt. " +
                "Der Echtbetrieb wird nach Abnahme durch die Post freigeschaltet." });
        var produkt = dto.ProduktNummer ?? s!.ProduktNummer;
        if (produkt is not > 0)
            return BadRequest(new { error = "PRODUKT", message = "Bitte zuerst ein Produkt wählen (Produkte laden)." });

        var (b, bFehler) = await BaueBriefAsync(dto, s!.FensterRechts);
        if (bFehler != null) return bFehler;

        var methode = bestellen ? "new_order" : "new_order_preview";
        var referenz = $"OC-{b!.EmployeeId?.ToString() ?? "X"}-{DateTime.Now:yyyyMMddHHmmss}";
        var envelope = WebStampSoap.Brief(methode, z!,
            new WebStampSoap.BriefAuftrag(produkt.Value, b.Pdf, referenz, "OneCrew " + b.Betreff));
        var a = await _client.RufeAsync(WebStampEndpunkte.Url(s.Umgebung), methode, envelope, ct);

        WebStampSoap.Auftrag? auftrag = a.Ok ? WebStampSoap.LiesAuftrag(a.Resultat!) : null;
        var ungueltig = auftrag?.Sendungen.Where(x => x.Status == "invalid").ToList() ?? new();
        var log = new WebStampAuftrag
        {
            ErstelltAm = DateTime.Now,
            ErstelltVon = int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid) ? uid : null,
            Umgebung = s.Umgebung,
            Art = bestellen ? "bestellung" : "vorschau",
            EmployeeId = b.EmployeeId,
            Empfaenger = string.Join(" · ", b.Empfaenger),
            Betreff = b.Betreff,
            ProduktNummer = produkt.Value,
            Referenz = referenz,
            Ok = a.Ok && ungueltig.Count == 0,
            OrderId = auftrag?.OrderId,
            Preis = auftrag?.Preis,
            Meldung = a.Ok
                ? (ungueltig.Count > 0 ? string.Join("; ", ungueltig.Select(x => x.Grund ?? "ungültig")) : null)
                : $"{a.Fault?.FehlerNummer} {a.Fehler}".Trim(),
            BriefPdf = bestellen && a.Ok ? b.Pdf : null,
        };
        _db.WebStampAuftraege.Add(log);
        await _db.SaveChangesAsync(ct);

        if (!a.Ok) return Ok(new { ok = false, auftragId = log.Id, fehler = a.Fehler,
                                   fehlerNummer = a.Fault?.FehlerNummer, requestId = a.Fault?.RequestId,
                                   dauerMs = a.DauerMs });
        return Ok(new
        {
            ok = true,
            auftragId = log.Id,
            art = log.Art,
            dauerMs = a.DauerMs,
            orderId = auftrag!.OrderId,
            preis = auftrag.Preis,
            einzelPreis = auftrag.EinzelPreis,
            referenz = auftrag.Referenz,
            gueltigBis = auftrag.GueltigBis,
            preise = auftrag.Preise,
            sendungen = auftrag.Sendungen,
            nachrichten = auftrag.Nachrichten,
            frankiervermerke = auftrag.Frankiervermerke,
            // Vorschau: die Post gibt den Brief mit eingesetzter Frankatur zur Kontrolle zurück.
            druckPdf = auftrag.Druckdaten != null ? Convert.ToBase64String(auftrag.Druckdaten) : null,
        });
    }

    // ── Protokoll ────────────────────────────────────────────────────────────

    [HttpGet("auftraege")]
    public async Task<IActionResult> Auftraege()
    {
        var liste = await _db.WebStampAuftraege.AsNoTracking()
            .OrderByDescending(x => x.Id).Take(50)
            .Select(x => new { x.Id, x.ErstelltAm, x.Umgebung, x.Art, x.EmployeeId, x.Empfaenger, x.Betreff,
                               x.ProduktNummer, x.Referenz, x.Ok, x.OrderId, x.Preis, x.Meldung,
                               hatPdf = x.BriefPdf != null })
            .ToListAsync();
        return Ok(liste);
    }

    [HttpGet("auftraege/{id:int}/pdf")]
    public async Task<IActionResult> AuftragPdf(int id)
    {
        var pdf = await _db.WebStampAuftraege.AsNoTracking().Where(x => x.Id == id)
            .Select(x => x.BriefPdf).FirstOrDefaultAsync();
        if (pdf == null) return NotFound(new { error = "KEIN_PDF" });
        return File(pdf, "application/pdf", $"Brief-{id}.pdf");
    }
}
