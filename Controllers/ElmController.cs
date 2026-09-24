using System.Security.Claims;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services.Elm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Swissdec ELM 6.0 — Etappe E1 (Walter 27.08.2026): Verbindungstests
/// Ping + CheckInteroperability. NUR Admin (eigener Sidebar-Bereich
/// «Swissdec»). Rein manuell ausgelöste Calls (Richtlinien Kap. 4: Ping
/// nie automatisieren). Kein Lohn-Edit → EditLock-Audit unkritisch
/// (GET-frei, POSTs rufen nur externe Test-Endpunkte).
/// </summary>
[ApiController]
[Authorize(Roles = "admin")]
[Route("api/elm")]
public class ElmController : ControllerBase
{
    private readonly ElmTransmitterClient _client;
    private readonly ElmAnnualDeclarationBuilder _builder;
    private readonly AppDbContext _db;
    public ElmController(ElmTransmitterClient client, ElmAnnualDeclarationBuilder builder, AppDbContext db)
    {
        _client = client;
        _builder = builder;
        _db = db;
    }

    /// <summary>
    /// Ziel des Aufrufs: entweder ein Schlüssel der hinterlegten Adressen
    /// («test» / «prod», siehe <see cref="ElmEndpunkte"/>) ODER eine frei
    /// eingegebene URL.
    ///
    /// Foundation-Test F01_01 verlangt, dass die Adresse nicht vom ENDBENUTZER
    /// verändert werden kann. Die Schranke dafür ist hier die Person, nicht das
    /// Feld: die Swissdec-Seite liegt im Bereich «Entwicklung», den nur ein
    /// Super-Admin vergeben kann (`UsersController.AreasMitEntwicklungsSchutz`),
    /// und beide Aufrufe unten prüfen `IsSuperAdmin` zusätzlich serverseitig.
    /// Ein normaler Admin kommt also weder an die Seite noch an den Endpunkt.
    /// Für den Super-Admin bleibt das freie Feld bewusst offen — die Swissdec-
    /// Testinfrastruktur liefert im Lauf der Zertifizierung wechselnde
    /// Receiver-Adressen (Walter 24.09.2026).
    /// </summary>
    public record ElmZielDto(string? Ziel, string? Url = null);

    private static bool UrlOk(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u)
        && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp);

    /// <summary>Adresse + Anzeigename aus dem DTO: Schlüssel bevorzugt, sonst freie URL.</summary>
    private static (string? Url, string Name)? ZielAufloesen(ElmZielDto? dto)
    {
        var treffer = ElmEndpunkte.Finde(dto?.Ziel);
        if (treffer != null) return (treffer.Url, treffer.Name);
        var frei = (dto?.Url ?? "").Trim();
        if (UrlOk(frei)) return (frei, "Eigene Adresse");
        return null;
    }

    /// <summary>
    /// Nur der Superadmin darf die Swissdec-Testumgebung ansprechen
    /// (Foundation-Test F01_01): die Rolle «admin» allein genügt NICHT —
    /// dieselbe Grenze wie beim Bereich «Entwicklung» im UI.
    /// </summary>
    private async Task<bool> IstSuperAdminAsync()
    {
        var id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(id, out var userId)) return false;
        return await _db.AppUsers.AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.IsSuperAdmin);
    }

    private IActionResult NurSuperAdmin()
        => StatusCode(403, new { error = "NUR_SUPERADMIN",
             message = "Die Swissdec-Verbindung ist dem Superadmin vorbehalten." });

    /// <summary>Wählbare Ziele fürs UI — Name + Adresse, rein zur Anzeige.</summary>
    [HttpGet("endpunkte")]
    public async Task<IActionResult> Endpunkte()
    {
        if (!await IstSuperAdminAsync()) return NurSuperAdmin();
        return Ok(ElmEndpunkte.Alle.Select(z => new { z.Schluessel, z.Name, z.Url, z.IstTest }));
    }

    [HttpPost("ping")]
    public async Task<IActionResult> Ping([FromBody] ElmZielDto dto, CancellationToken ct)
    {
        if (!await IstSuperAdminAsync()) return NurSuperAdmin();
        var ziel = ZielAufloesen(dto);
        if (ziel == null)
            return BadRequest(new { error = "ZIEL_UNBEKANNT",
                message = "Bitte «test» oder «prod» wählen oder eine gültige Adresse eingeben." });
        var r = await _client.PingAsync(ziel.Value.Url!, ct);
        return Ok(new { name = ziel.Value.Name, url = ziel.Value.Url, ergebnis = r });
    }

    [HttpPost("check-interoperability")]
    public async Task<IActionResult> CheckInteroperability([FromBody] ElmZielDto dto, CancellationToken ct)
    {
        if (!await IstSuperAdminAsync()) return NurSuperAdmin();
        var ziel = ZielAufloesen(dto);
        if (ziel == null)
            return BadRequest(new { error = "ZIEL_UNBEKANNT",
                message = "Bitte «test» oder «prod» wählen oder eine gültige Adresse eingeben." });
        var r = await _client.CheckInteroperabilityAsync(ziel.Value.Url!, ct);
        return Ok(new { name = ziel.Value.Name, url = ziel.Value.Url, ergebnis = r });
    }

    /// <summary>
    /// E2: Jahresmeldung AHV als DeclareAnnualSalary-XML erzeugen + gegen
    /// die ELM-6.0-Schemas validieren. Nur mit KUNSTDATEN (test.onecrew.ch)
    /// im Refapps-Transmitter hochladen — nie Echtdaten.
    /// </summary>
    [HttpGet("annual-ahv/{year:int}")]
    public async Task<IActionResult> AnnualAhv(int year, CancellationToken ct)
    {
        if (year < 2020 || year > 2100)
            return BadRequest(new { error = "YEAR_INVALID", message = "Bitte ein gültiges Jahr angeben." });
        var r = await _builder.BuildAhvAsync(year, ct);
        return Ok(new
        {
            xml = r.Xml,
            personen = r.Personen,
            uebersprungen = r.Uebersprungen,
            totalAhv = r.TotalAhv,
            totalAlv = r.TotalAlv,
            warnungen = r.Warnungen,
            xsdFehler = r.XsdFehler,
            valid = r.XsdFehler.Count == 0 && r.Xml.Length > 0
        });
    }

    // ── E3: Stammdaten Rechtseinheit (Walter 28.08.2026) ──────────────
    // EINE Zeile (Meldeeinheit). Kein Lohn-Edit → EditLock unkritisch
    // (Controller ist in der Audit-Whitelist).

    public record ElmStammdatenDto(
        string? Uid,
        string? AkName, string? AkKassenNummer, string? AkAbrechnungsNummer,
        string? FakKassenNummer, string? FakAbrechnungsNummer,
        string? UvgVersicherer, string? UvgVersichererNummer, string? UvgKundenNummer, string? UvgVertragsNummer,
        string? UvgUid, DateOnly? UvgVersichertSeit,
        string? UvgzVersicherer, string? UvgzVersichererNummer, string? UvgzKundenNummer, string? UvgzVertragsNummer,
        string? KtgVersicherer, string? KtgVersichererNummer, string? KtgKundenNummer, string? KtgVertragsNummer,
        string? BvgVersicherer, string? BvgVersichererNummer, string? BvgKundenNummer, string? BvgVertragsNummer,
        string? BvgUid, DateOnly? BvgVersichertSeit);

    [HttpGet("stammdaten")]
    public async Task<IActionResult> GetStammdaten(CancellationToken ct)
    {
        var s = await _db.ElmStammdaten.AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        return Ok(s ?? new ElmStammdaten());
    }

    /// <summary>
    /// Vorschlag aus dem Empfänger-Katalog (Walter 28.08.2026): die zentral
    /// gepflegten Lohndatenempfänger liefern Name, Kassen-/Versicherer-Nr.
    /// und UID; Mitglied-/Subnummer nur, wenn sie über ALLE Filialen gleich
    /// sind (sonst Hinweis — pro-Filiale-Zuordnung kommt in E5).
    /// </summary>
    [HttpGet("stammdaten/vorschlag")]
    public async Task<IActionResult> StammdatenVorschlag(CancellationToken ct)
    {
        var empf = await _db.LohndatenEmpfaengers.AsNoTracking()
            .Include(e => e.Zuordnungen)
            .Where(e => e.IsActive)
            .OrderBy(e => e.Id)
            .ToListAsync(ct);

        var hinweise = new List<string>();
        var dto = new Dictionary<string, string?>();

        // Mitglied-/Subnummer nur übernehmen, wenn filialübergreifend identisch.
        (string? mitglied, string? sub) Gemeinsam(LohndatenEmpfaenger e)
        {
            var heute = DateOnly.FromDateTime(DateTime.Today);
            var akt = e.Zuordnungen.Where(z => z.GiltAm(heute)).ToList();
            if (akt.Count == 0) return (null, null);
            var m = akt.Select(z => (z.Mitgliednummer ?? "").Trim()).Distinct().ToList();
            var su = akt.Select(z => (z.Subnummer ?? "").Trim()).Distinct().ToList();
            var mg = m.Count == 1 && m[0].Length > 0 ? m[0] : null;
            var sg = su.Count == 1 && su[0].Length > 0 ? su[0] : null;
            if (m.Count > 1)
                hinweise.Add($"«{e.Bezeichnung}»: Mitgliednummern sind pro Filiale unterschiedlich — bleiben leer (Zuordnung pro Filiale folgt in E5).");
            return (mg, sg);
        }

        void Fill(string prefix, LohndatenEmpfaenger? e, bool mitUid)
        {
            if (e == null) return;
            var (mg, sg) = Gemeinsam(e);
            dto[prefix + "Versicherer"] = e.Bezeichnung;
            dto[prefix + "VersichererNummer"] = e.Kassennummer;
            dto[prefix + "KundenNummer"] = mg;
            dto[prefix + "VertragsNummer"] = sg;
            if (mitUid) dto[prefix + "Uid"] = e.UidNummer;
        }

        var ak = empf.FirstOrDefault(e => e.Art == "AUSGLEICHSKASSE");
        if (ak != null)
        {
            var (mg, _) = Gemeinsam(ak);
            dto["akName"] = ak.Bezeichnung;
            dto["akKassenNummer"] = ak.Kassennummer;
            dto["akAbrechnungsNummer"] = mg;
        }
        var fak = empf.FirstOrDefault(e => e.Art == "FAK");
        if (fak != null)
        {
            var (mg, _) = Gemeinsam(fak);
            dto["fakKassenNummer"] = fak.Kassennummer;
            dto["fakAbrechnungsNummer"] = mg;
        }

        // UVG-Zusatz: eigener Art-Code «UVGZ» (seit 28.08.2026); Alt-Einträge
        // mit Art «UVG» + «Zusatz» im Namen werden weiterhin erkannt.
        bool IstZusatz(LohndatenEmpfaenger e) =>
            (e.Bezeichnung + " " + (e.Zusatz ?? "")).ToLowerInvariant().Contains("zusatz");
        Fill("uvg",  empf.FirstOrDefault(e => e.Art == "UVG" && !IstZusatz(e)), mitUid: true);
        Fill("uvgz", empf.FirstOrDefault(e => e.Art == "UVGZ")
                     ?? empf.FirstOrDefault(e => e.Art == "UVG" && IstZusatz(e)), mitUid: false);
        Fill("ktg",  empf.FirstOrDefault(e => e.Art == "KTG"), mitUid: false);
        Fill("bvg",  empf.FirstOrDefault(e => e.Art == "BVG"), mitUid: true);

        if (dto.Count == 0)
            hinweise.Add("Im Empfänger-Katalog (Filiale → Lohndaten Empfänger) sind noch keine aktiven Empfänger erfasst.");

        return Ok(new { werte = dto, hinweise });
    }

    [HttpPut("stammdaten")]
    public async Task<IActionResult> SaveStammdaten([FromBody] ElmStammdatenDto dto, CancellationToken ct)
    {
        var uid = (dto.Uid ?? "").Trim();
        if (uid.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(uid, @"^CHE-\d{3}\.\d{3}\.\d{3}$"))
            return BadRequest(new { error = "UID_INVALID", message = "UID bitte im Format CHE-XXX.XXX.XXX erfassen." });

        var s = await _db.ElmStammdaten.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        if (s == null) { s = new ElmStammdaten(); _db.ElmStammdaten.Add(s); }

        static string? T(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        s.Uid = T(uid);
        s.AkName = T(dto.AkName); s.AkKassenNummer = T(dto.AkKassenNummer); s.AkAbrechnungsNummer = T(dto.AkAbrechnungsNummer);
        s.FakKassenNummer = T(dto.FakKassenNummer); s.FakAbrechnungsNummer = T(dto.FakAbrechnungsNummer);
        s.UvgVersicherer = T(dto.UvgVersicherer); s.UvgVersichererNummer = T(dto.UvgVersichererNummer); s.UvgKundenNummer = T(dto.UvgKundenNummer); s.UvgVertragsNummer = T(dto.UvgVertragsNummer);
        s.UvgUid = T(dto.UvgUid); s.UvgVersichertSeit = dto.UvgVersichertSeit;
        s.UvgzVersicherer = T(dto.UvgzVersicherer); s.UvgzVersichererNummer = T(dto.UvgzVersichererNummer); s.UvgzKundenNummer = T(dto.UvgzKundenNummer); s.UvgzVertragsNummer = T(dto.UvgzVertragsNummer);
        s.KtgVersicherer = T(dto.KtgVersicherer); s.KtgVersichererNummer = T(dto.KtgVersichererNummer); s.KtgKundenNummer = T(dto.KtgKundenNummer); s.KtgVertragsNummer = T(dto.KtgVertragsNummer);
        s.BvgVersicherer = T(dto.BvgVersicherer); s.BvgVersichererNummer = T(dto.BvgVersichererNummer); s.BvgKundenNummer = T(dto.BvgKundenNummer); s.BvgVertragsNummer = T(dto.BvgVertragsNummer);
        s.BvgUid = T(dto.BvgUid); s.BvgVersichertSeit = dto.BvgVersichertSeit;
        s.UpdatedAt = DateTime.Now;
        s.UpdatedBy = User.FindFirst(ClaimTypes.Name)?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        await _db.SaveChangesAsync(ct);
        return Ok(s);
    }
}
