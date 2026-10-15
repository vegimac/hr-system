using System.Security.Cryptography.X509Certificates;
using HrSystem.Data;
using HrSystem.Services.Elm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// SUA-Zertifikat für den Benutzer (Walter 15.10.2026, Swissdec-Zertifizierung): Laufzeit sehen,
/// vor Ablauf gewarnt werden, mit einem Knopf erneuern — ohne Fachbegriffe und ohne Adresswahl.
/// Admin genügt; die Zieladresse kommt aus dem Fall bzw. <see cref="ElmEndpunkte.Standard"/>,
/// wählen kann sie weiterhin nur der Superadmin (F01_01). Regeln: <see cref="ElmZertifikatLaufzeit"/>.
/// </summary>
[ApiController]
[Authorize(Roles = "admin")]
[Route("api/elm/zertifikat")]
public class ElmZertifikatController : ControllerBase
{
    private readonly ElmSuaService _sua;
    private readonly ElmZertifikatStore _store;
    private readonly AppDbContext _db;

    public ElmZertifikatController(ElmSuaService sua, ElmZertifikatStore store, AppDbContext db)
    {
        _sua = sua;
        _store = store;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Stand(CancellationToken ct) => Ok(await StandAsync(ct));

    [HttpPost("erneuern")]
    public async Task<IActionResult> Erneuern(CancellationToken ct)
    {
        var fall = _store.LadeFall();
        if (fall == null)
            return BadRequest(new { error = "KEIN_FALL", message = "Es gibt noch keine Registrierung — zuerst «Neu registrieren»." });
        var vorher = SuaBis();
        try
        {
            var r = await _sua.SynchronizeAsync(ZielUrl(fall), null, renew: true, ct);
            var ok = SuaBis() is DateTime nachher && nachher != vorher;
            return Ok(new
            {
                ok,
                meldung = ok ? r.Meldung : "Nicht erneuert — " + (r.Meldung ?? "keine Antwort von Swissdec."),
                stand = await StandAsync(ct),
                ergebnis = r.Call,
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = "ERNEUERN", message = ex.Message });
        }
    }

    public record RegistrierenBody(string? Uid, string? Firma, string? Kontakt, string? Plz, string? Ort,
        string? Domain, string? Versicherer, string? Kundennummer, string? Vertragsnummer,
        string? AddresseeIdentification);

    [HttpPost("registrieren")]
    public async Task<IActionResult> Registrieren([FromBody] RegistrierenBody dto, CancellationToken ct)
    {
        var url = ZielUrl(_store.LadeFall());
        try
        {
            var r = await _sua.RegisterAsync(url, new ElmSuaRegisterDto(
                dto.Uid, dto.Firma, dto.Kontakt, dto.Plz, dto.Ort, dto.AddresseeIdentification,
                dto.Domain, dto.Versicherer, dto.Kundennummer, dto.Vertragsnummer, AlsTestfall: false), ct);
            var neu = r.Fall is { Abgeschlossen: false, RegistriertAm: not null } f && f.RegistriertAm > DateTime.Now.AddMinutes(-5);
            return Ok(new
            {
                ok = neu,
                meldung = neu
                    ? "Registrierung gesendet. Swissdec prüft jetzt die Angaben und schickt danach das Registrierungspasswort per Brief."
                    : r.Meldung,
                stand = await StandAsync(ct),
                ergebnis = r.Call,
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = "REGISTRIEREN", message = ex.Message });
        }
    }

    [HttpPost("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var fall = _store.LadeFall();
        if (fall == null)
            return BadRequest(new { error = "KEIN_FALL", message = "Es gibt noch keine Registrierung." });
        try
        {
            var r = await _sua.SynchronizeAsync(ZielUrl(fall), null, renew: false, ct);
            var state = r.State ?? r.Fall?.LetzterState;
            return Ok(new
            {
                ok = r.State != null,
                meldung = r.State != null ? StateText(r.State) : r.Meldung,
                // Auf der Testinfrastruktur kommt das «Brief»-Passwort in der Antwort (Code 9998).
                passwortVorschlag = r.Meldungen?.Einmalpasswort,
                stand = await StandAsync(ct),
                ergebnis = r.Call,
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = "STATUS", message = ex.Message });
        }
    }

    public record AbschliessenBody(string? Passwort);

    [HttpPost("abschliessen")]
    public async Task<IActionResult> Abschliessen([FromBody] AbschliessenBody dto, CancellationToken ct)
    {
        var fall = _store.LadeFall();
        if (fall == null)
            return BadRequest(new { error = "KEIN_FALL", message = "Es gibt noch keine Registrierung." });
        if (string.IsNullOrWhiteSpace(dto.Passwort))
            return BadRequest(new { error = "PASSWORT_FEHLT", message = "Bitte das Registrierungspasswort aus dem Brief von Swissdec eingeben." });
        var vorher = SuaBis();
        try
        {
            var r = await _sua.SynchronizeAsync(ZielUrl(fall), dto.Passwort, renew: false, ct);
            var ok = SuaBis() is DateTime nachher && nachher != vorher;
            return Ok(new
            {
                ok,
                meldung = ok ? r.Meldung : "Kein Zertifikat erhalten — " + (r.Meldung ?? "keine Antwort von Swissdec."),
                stand = await StandAsync(ct),
                ergebnis = r.Call,
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = "ABSCHLIESSEN", message = ex.Message });
        }
    }

    // ── Stand ────────────────────────────────────────────────────────────────

    private static string ZielUrl(ElmSuaFall? fall)
        => ElmEndpunkte.IstSicher(fall?.Url) ? fall!.Url! : ElmEndpunkte.Standard().Url;

    private DateTime? SuaBis()
    {
        try { using var z = _store.LadeSua(); return z?.NotAfter; }
        catch { return null; }
    }

    public static string StateText(string? state) => (state ?? "").ToLowerInvariant() switch
    {
        "" or "processing" => "Swissdec prüft die Angaben (Firma, UID, Versicherungsvertrag).",
        "registered" => "Angaben geprüft. Swissdec schickt das Registrierungspasswort per Brief an die Firma.",
        "verified"   => "Bereit: Registrierungspasswort aus dem Brief eingeben.",
        "rejected"   => "Swissdec hat die Registrierung abgelehnt — Angaben prüfen und neu registrieren.",
        "expired"    => "Die Registrierung ist abgelaufen — neu registrieren.",
        _            => $"Stand: {state}",
    };

    private async Task<object> StandAsync(CancellationToken ct)
    {
        var jetzt = DateTime.Now;
        var fall = _store.LadeFall();
        X509Certificate2? sua = null, erp = null;
        try { sua = _store.LadeSua(); } catch { /* unlesbar = keines */ }
        try { erp = _store.LadeErp(); } catch { /* dito */ }

        var l = ElmZertifikatLaufzeit.Beurteile(sua?.NotBefore, sua?.NotAfter, sua?.HasPrivateKey == true,
            fall?.Erneuerungen ?? 0, jetzt);

        var laufend = fall != null && !fall.Abgeschlossen
            && (sua == null || (fall.RegistriertAm is DateTime reg && reg > sua.NotBefore));

        var hs = await _db.Hauptsitze.AsNoTracking().Where(h => h.IsActive).OrderBy(h => h.Id).FirstOrDefaultAsync(ct);
        var ziel = ElmEndpunkte.FindeNachUrl(ZielUrl(fall)) ?? ElmEndpunkte.Standard();

        object? erpTeil = null;
        if (erp != null)
        {
            var rest = (int)Math.Floor((erp.NotAfter - jetzt).TotalDays);
            erpTeil = new
            {
                gueltigBis = erp.NotAfter,
                restTage = rest,
                stufe = rest < 0 ? ElmZertifikatLaufzeit.Rot : rest <= ElmZertifikatLaufzeit.FensterTage ? ElmZertifikatLaufzeit.Warn : ElmZertifikatLaufzeit.Ok,
                aussteller = erp.GetNameInfo(X509NameType.SimpleName, true),
            };
        }

        var stand = new
        {
            ziel = new { ziel.Name, ziel.IstTest },
            sua = new
            {
                vorhanden = sua != null,
                gueltigAb = sua?.NotBefore,
                l.GueltigBis, l.RestTage, l.Stufe, l.Aktion, l.Text, l.ErneuernAb, l.Erneuerungen,
                maxErneuerungen = ElmZertifikatLaufzeit.MaxErneuerungen,
                l.ErneuernErlaubt,
                aussteller = sua?.GetNameInfo(X509NameType.SimpleName, true),
            },
            erp = erpTeil,
            auto = new { letzterVersuch = fall?.AutoVersuchAm, fehler = fall?.AutoFehler },
            registrierung = new
            {
                laufend,
                state = laufend ? fall!.LetzterState : null,
                text = laufend ? StateText(fall!.LetzterState) : null,
                registriertAm = fall?.RegistriertAm,
            },
            vorlage = new
            {
                uid = fall?.Uid ?? hs?.Uid,
                firma = fall?.CompanyName ?? hs?.Name,
                kontakt = fall?.ContactName ?? hs?.KontaktName,
                plz = fall?.Zip ?? hs?.Plz,
                ort = fall?.City ?? hs?.Ort,
                domain = fall?.Domain ?? ElmSuaService.StandardDomain,
                versicherer = fall?.InsuranceName,
                kundennummer = fall?.CustomerIdentity,
                vertragsnummer = fall?.ContractIdentity,
                addresseeIdentification = fall?.AddresseeIdentification,
            },
            domains = ElmSuaService.Domains,
        };
        sua?.Dispose();
        erp?.Dispose();
        return stand;
    }
}
