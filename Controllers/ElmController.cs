using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
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
    private readonly ElmMonthlyDeclarationBuilder _monatsBuilder;
    private readonly ElmEinstellungen _einstellungen;
    private readonly ElmSuaService _sua;
    private readonly ElmZertifikatStore _store;
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    public ElmController(ElmTransmitterClient client, ElmAnnualDeclarationBuilder builder,
        ElmMonthlyDeclarationBuilder monatsBuilder,
        ElmSuaService sua, ElmZertifikatStore store, AppDbContext db,
        ElmEinstellungen einstellungen, IMemoryCache cache)
    {
        _cache = cache;
        _einstellungen = einstellungen;
        _client = client;
        _builder = builder;
        _monatsBuilder = monatsBuilder;
        _sua = sua;
        _store = store;
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
    public record ElmZielDto(string? Ziel, string? Url = null, int? VersatzSekunden = null,
        string? ZweiterOperand = null);

    /// <summary>
    /// Simulierter Zeitversatz für den Foundation-Test F01_03 (Walter 24.09.2026):
    /// Der Wert verstellt die Zeit, die wir SENDEN, und zugleich unsere
    /// Vergleichsbasis — das Programm verhält sich also wie mit einer falsch
    /// gehenden Serveruhr und muss ab 60 Sekunden die Fehlermeldung zeigen.
    /// Gilt nur für diesen einen Aufruf, wird nirgends gespeichert. Grenze
    /// ±24 Stunden, damit kein Unsinn ins XML gerät.
    /// </summary>
    private static int Versatz(ElmZielDto? dto)
        => Math.Clamp(dto?.VersatzSekunden ?? 0, -86400, 86400);

    // Nur https (Foundation F02_01 «Transportsicherheit») — siehe ElmEndpunkte.IstSicher.
    private static bool UrlOk(string? url) => ElmEndpunkte.IstSicher(url);

    /// <summary>Adresse + Anzeigename aus dem DTO: Schlüssel bevorzugt, sonst freie URL.</summary>
    private static (string? Url, string Name)? ZielAufloesen(ElmZielDto? dto)
    {
        var treffer = ElmEndpunkte.Finde(dto?.Ziel);
        if (treffer != null) return (treffer.Url, treffer.Name);
        var frei = (dto?.Url ?? "").Trim();
        var bekannt = ElmEndpunkte.FindeNachUrl(frei);
        if (bekannt != null) return (bekannt.Url, bekannt.Name);
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
                message = "Bitte «test» oder «prod» wählen oder eine gültige https-Adresse eingeben "
                        + "(unverschlüsseltes http ist nicht zulässig)." });
        var r = await _client.PingAsync(ziel.Value.Url!, Versatz(dto), ct);
        return Ok(new { name = ziel.Value.Name, url = ziel.Value.Url, ergebnis = r });
    }

    [HttpPost("check-interoperability")]
    public async Task<IActionResult> CheckInteroperability([FromBody] ElmZielDto dto, CancellationToken ct)
    {
        if (!await IstSuperAdminAsync()) return NurSuperAdmin();
        var ziel = ZielAufloesen(dto);
        if (ziel == null)
            return BadRequest(new { error = "ZIEL_UNBEKANNT",
                message = "Bitte «test» oder «prod» wählen oder eine gültige https-Adresse eingeben "
                        + "(unverschlüsseltes http ist nicht zulässig)." });
        // Der zweite Operand ist die einzige Eingabe dieses Aufrufs (Foundation F03_02);
        // unlesbar ⇒ 0.01, der erste Vorschlag aus der Prüfliste.
        var zweiter = ElmInterop.LiesBetrag(dto.ZweiterOperand) ?? 0.01m;
        var r = await _client.CheckInteroperabilityAsync(ziel.Value.Url!, zweiter, Versatz(dto), ct);
        return Ok(new { name = ziel.Value.Name, url = ziel.Value.Url,
                        ersterOperand = ElmInterop.Betrag(ElmInterop.FirstOperand),
                        zweiterOperand = ElmInterop.Betrag(zweiter),
                        umlautString = ElmInterop.UmlautString,
                        ergebnis = r });
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

    /// <summary>
    /// E6: Monatsmeldung (Quellensteuer + Statistik) als DeclareMonthlySalary-XML,
    /// gegen die ELM-6.0-Schemas validiert und — wenn eine Referenz von Swissdec
    /// vorliegt — Feld für Feld mit ihr verglichen. Nur KUNSTDATEN.
    /// </summary>
    [Authorize(Roles = "admin,superuser")]
    [HttpGet("monthly/{year:int}/{month:int}")]
    public async Task<IActionResult> Monthly(int year, int month, CancellationToken ct)
    {
        if (year < 2020 || year > 2100 || month < 1 || month > 12)
            return BadRequest(new { error = "PERIODE_UNGUELTIG", message = "Bitte Jahr und Monat gültig angeben." });

        var r = await _monatsBuilder.BuildAsync(year, month, ct);

        // Referenz von der Platte, wenn eine da ist. Im veröffentlichten Programm
        // gibt es den Ordner SWISSCEC nicht — dort wird die Datei hochgeladen
        // (POST …/vergleichen), und diese Antwort sagt es dem Benutzer.
        var refDatei = ReferenzDatei(year, month);
        string? refXml = refDatei == null ? null : await System.IO.File.ReadAllTextAsync(refDatei, ct);
        return Ok(MonatsAntwort(r, year, month,
            refXml, refDatei == null ? null : System.IO.Path.GetFileName(refDatei)));
    }

    /// <summary>
    /// Denselben Monat mit einer HOCHGELADENEN Referenz vergleichen (Walter 27.09.2026)
    /// — ein Monat, eine Datei. Die Datei wird nur für diesen Aufruf gelesen.
    /// </summary>
    [Authorize(Roles = "admin,superuser")]
    [HttpPost("monthly/{year:int}/{month:int}/vergleichen")]
    [RequestSizeLimit(16 * 1024 * 1024)]
    public async Task<IActionResult> MonthlyVergleichen(int year, int month,
        [FromForm] IFormFile? referenz, CancellationToken ct)
    {
        if (year < 2020 || year > 2100 || month < 1 || month > 12)
            return BadRequest(new { error = "PERIODE_UNGUELTIG", message = "Bitte Jahr und Monat gültig angeben." });
        if (referenz == null || referenz.Length == 0)
            return BadRequest(new { error = "KEINE_REFERENZ", message = "Bitte eine Referenz-Datei auswählen." });

        var name = System.IO.Path.GetFileName(referenz.FileName ?? "Referenz.xml");
        var (dJahr, dMonat) = MonatAusDateiname(name);
        // Falscher Monat ergäbe hunderte Scheinunterschiede — lieber gleich sagen.
        if (dJahr != 0 && (dJahr != year || dMonat != month))
            return BadRequest(new { error = "MONAT_PASST_NICHT",
                message = $"«{name}» ist die Referenz für {dMonat:00}.{dJahr}, verglichen wird aber {month:00}.{year}. "
                        + "Bitte die Datei zu diesem Monat wählen." });

        var r = await _monatsBuilder.BuildAsync(year, month, ct);
        using var leser = new StreamReader(referenz.OpenReadStream());
        var refXml = await leser.ReadToEndAsync(ct);
        return Ok(MonatsAntwort(r, year, month, refXml, name));
    }

    /// <summary>Antwort für einen Monat — mit Vergleich, wenn eine Referenz vorliegt.</summary>
    private static object MonatsAntwort(ElmMonthlyDeclarationBuilder.BuildResult r,
        int year, int month, string? refXml, string? refName)
    {
        object? vergleich = null;
        string? bericht = null;
        if (r.Xml.Length > 0 && refXml != null)
        {
            try
            {
                var e = ElmXmlVergleich.Vergleiche(r.Xml, refXml);
                bericht = ElmXmlVergleich.Bericht(e, $"ELM-Monatsmeldung {year}-{month:00}", refName ?? "Referenz");
                vergleich = new
                {
                    referenz = refName,
                    geprueft = e.Geprueft, offen = e.Offen, bewusst = e.Bewusst,
                    personenIst = e.PersonenIst, personenSoll = e.PersonenSoll,
                    unterschiede = e.Unterschiede.Take(400).Select(u => new
                    {
                        person = u.Person, feld = u.Feld, ist = u.Ist, soll = u.Soll,
                        bewusst = u.Bewusst
                    })
                };
            }
            catch (Exception ex)
            {
                vergleich = new { fehler = $"Referenz nicht lesbar: {ex.Message}" };
            }
        }

        return new
        {
            xml = r.Xml,
            personen = r.Personen,
            qstZeilen = r.QstZeilen,
            statistikZeilen = r.StatistikZeilen,
            warnungen = r.Warnungen,
            xsdFehler = r.XsdFehler,
            valid = r.XsdFehler.Count == 0 && r.Xml.Length > 0,
            referenzFehlt = r.Xml.Length > 0 && refXml == null,
            vergleich,
            bericht
        };
    }

    /// <summary>
    /// Monate, für die eine Meldung erzeugt werden kann: in ALLEN Filialen der
    /// Rechtseinheit definitiv abgeschlossen. Alles andere wäre eine Meldung mit
    /// halben Daten.
    /// </summary>
    [Authorize(Roles = "admin,superuser")]
    [HttpGet("monthly/moegliche-monate")]
    public async Task<IActionResult> MoeglicheMonate(CancellationToken ct)
    {
        var filialen = await _db.CompanyProfiles.AsNoTracking().Select(c => c.Id).ToListAsync(ct);
        var perioden = await _db.PayrollPerioden.AsNoTracking()
            .Where(p => filialen.Contains(p.CompanyProfileId))
            .Select(p => new { p.Year, p.Month, p.Status, p.CompanyProfileId })
            .ToListAsync(ct);
        var monate = perioden
            .GroupBy(p => new { p.Year, p.Month })
            .OrderByDescending(g => g.Key.Year).ThenByDescending(g => g.Key.Month)
            .Select(g => new
            {
                jahr = g.Key.Year,
                monat = g.Key.Month,
                filialen = g.Select(x => x.CompanyProfileId).Distinct().Count(),
                offen = g.Count(x => x.Status != "abgeschlossen"),
                bereit = g.All(x => x.Status == "abgeschlossen"),
                referenz = ReferenzDatei(g.Key.Year, g.Key.Month) != null
            })
            .ToList();
        return Ok(new { monate });
    }

    /// <summary>
    /// Alle Monate, zu denen eine Referenz von Swissdec im Repo liegt, in EINEM
    /// Durchgang erzeugen und vergleichen (Walter 27.09.2026). Der Einzelmonat sagt,
    /// ob ein Monat stimmt; erst der Durchgang über alle zeigt, ob ein Fehler einmalig
    /// ist oder sich durch das Jahr zieht.
    ///
    /// <para>Rein lesend: es wird nichts gespeichert und nichts gesendet.</para>
    /// </summary>
    [Authorize(Roles = "admin,superuser")]
    [HttpGet("monthly/alle-pruefen")]
    public async Task<IActionResult> AlleMonatePruefen(CancellationToken ct)
    {
        var quellen = new List<(int Jahr, int Monat, string Name, string Xml)>();
        foreach (var (jahr, monat, datei) in ReferenzMonate())
            quellen.Add((jahr, monat, System.IO.Path.GetFileName(datei),
                         await System.IO.File.ReadAllTextAsync(datei, ct)));

        if (quellen.Count == 0)
            return Ok(new
            {
                monate = new List<object>(), bericht = (string?)null,
                zusammenfassung = new { gesamt = 0, fertig = 0, mitOffenen = 0, nichtBereit = 0 },
                dateienNoetig = true,
                hinweis = "Die Referenzen von Swissdec liegen nicht auf diesem Server — "
                        + "sie gehören nicht auf eine Lohnanlage. Bitte die Dateien aus dem Ordner "
                        + "SWISSCEC/RefXML auswählen (alle RefXML_…_MONTHLY.xml auf einmal)."
            });

        return await VergleicheMonate(quellen, ct);
    }

    /// <summary>
    /// Dasselbe mit hochgeladenen Referenzen (Walter 27.09.2026). Der Ordner
    /// SWISSCEC/RefXML liegt im Quellcode, nicht im veröffentlichten Programm —
    /// und 7 MB Swissdec-Übungsdaten haben auf der Produktion auch nichts verloren.
    /// Die Dateien werden nur für diesen Aufruf gelesen und nirgends gespeichert.
    /// </summary>
    [Authorize(Roles = "admin,superuser")]
    [HttpPost("monthly/alle-pruefen")]
    [RequestSizeLimit(64 * 1024 * 1024)]
    public async Task<IActionResult> AlleMonatePruefenMitDateien(
        [FromForm] List<IFormFile> dateien, CancellationToken ct)
    {
        var quellen = new List<(int Jahr, int Monat, string Name, string Xml)>();
        var uebergangen = new List<string>();

        foreach (var f in dateien ?? new List<IFormFile>())
        {
            var name = System.IO.Path.GetFileName(f.FileName ?? "");
            var (jahr, monat) = MonatAusDateiname(name);
            if (jahr == 0)
            {
                uebergangen.Add($"{name} — kein Monat im Namen erkennbar (erwartet RefXML_JJJJ-MM_MONTHLY.xml).");
                continue;
            }
            if (quellen.Any(q => q.Jahr == jahr && q.Monat == monat))
            {
                uebergangen.Add($"{name} — für {monat:00}.{jahr} liegt schon eine Referenz vor.");
                continue;
            }
            using var leser = new StreamReader(f.OpenReadStream());
            quellen.Add((jahr, monat, name, await leser.ReadToEndAsync(ct)));
        }

        if (quellen.Count == 0)
            return BadRequest(new { error = "KEINE_REFERENZ",
                message = "Keine brauchbare Referenz dabei. Erwartet werden die Dateien "
                        + "RefXML_JJJJ-MM_MONTHLY.xml aus dem Ordner SWISSCEC/RefXML.",
                uebergangen });

        quellen = quellen.OrderBy(q => q.Jahr).ThenBy(q => q.Monat).ToList();
        return await VergleicheMonate(quellen, ct, uebergangen);
    }

    /// <summary>
    /// Alle übergebenen Monate erzeugen und Feld für Feld mit ihrer Referenz
    /// vergleichen. Rein lesend: es wird nichts gespeichert und nichts gesendet.
    /// </summary>
    private async Task<IActionResult> VergleicheMonate(
        List<(int Jahr, int Monat, string Name, string Xml)> quellen,
        CancellationToken ct, List<string>? uebergangen = null)
    {
        var zeilen = new List<object>();
        var tabelle = new StringBuilder();
        var abschnitte = new StringBuilder();
        int fertig = 0, mitOffenen = 0, nichtBereit = 0;

        tabelle.AppendLine("| Monat | Personen | Felder | Offen | Bewusst | Schema |");
        tabelle.AppendLine("|---|---:|---:|---:|---:|---|");

        foreach (var (jahr, monat, refName, refXml) in quellen)
        {
            ct.ThrowIfCancellationRequested();
            var name = $"{monat:00}.{jahr}";
            var r = await _monatsBuilder.BuildAsync(jahr, monat, ct);

            // Kein XML = der Monat ist nicht meldebereit. Das ist KEIN Fehler der
            // Rechnung, sondern eine offene Lohnperiode — darum getrennt gezählt.
            if (r.Xml.Length == 0)
            {
                nichtBereit++;
                var grund = r.Warnungen.FirstOrDefault() ?? "Es wurde keine Meldung erzeugt.";
                zeilen.Add(new { jahr, monat, referenz = refName, bereit = false, grund });
                tabelle.AppendLine($"| {name} | — | — | — | — | nicht bereit |");
                abschnitte.AppendLine($"## {name} — nicht bereit");
                abschnitte.AppendLine();
                abschnitte.AppendLine(grund);
                abschnitte.AppendLine();
                continue;
            }

            var schema = r.XsdFehler.Count == 0 ? "ok" : $"{r.XsdFehler.Count} Fehler";

            ElmXmlVergleich.Ergebnis? e = null;
            string? lesefehler = null;
            try { e = ElmXmlVergleich.Vergleiche(r.Xml, refXml); }
            catch (Exception ex) { lesefehler = $"Referenz nicht lesbar: {ex.Message}"; }

            if (e == null)
            {
                zeilen.Add(new { jahr, monat, referenz = refName, bereit = true,
                    personen = r.Personen, xsdFehler = r.XsdFehler.Count, grund = lesefehler });
                tabelle.AppendLine($"| {name} | {r.Personen} | — | — | — | {schema} |");
                continue;
            }

            fertig++;
            if (e.Offen > 0) mitOffenen++;
            zeilen.Add(new
            {
                jahr, monat, referenz = refName, bereit = true,
                personen = r.Personen, personenSoll = e.PersonenSoll,
                geprueft = e.Geprueft, offen = e.Offen, bewusst = e.Bewusst,
                xsdFehler = r.XsdFehler.Count,
                warnungen = r.Warnungen,
                // Nur die OFFENEN in die Antwort — die bewussten stehen im Bericht.
                unterschiede = e.Unterschiede.Where(u => !u.IstBewusst).Take(80).Select(u => new
                {
                    person = u.Person, feld = u.Feld, ist = u.Ist, soll = u.Soll
                })
            });
            tabelle.AppendLine($"| {name} | {r.Personen} von {e.PersonenSoll} | {e.Geprueft} "
                             + $"| {(e.Offen == 0 ? "—" : $"**{e.Offen}**")} | {e.Bewusst} | {schema} |");

            // Aus «# 11.2024» wird «## 11.2024» — der Einzelbericht wird Abschnitt.
            abschnitte.AppendLine("#" + ElmXmlVergleich.Bericht(e, name, refName));
            abschnitte.AppendLine();
        }

        var sb = new StringBuilder();
        sb.AppendLine("# ELM-Monatsmeldungen gegen die Referenz");
        sb.AppendLine();
        sb.AppendLine($"Stand {DateTime.Now:dd.MM.yyyy HH:mm} · {quellen.Count} Monate mit Referenz · "
                    + $"{fertig} erzeugt und verglichen, {mitOffenen} davon mit offenen Unterschieden, "
                    + $"{nichtBereit} noch nicht meldebereit.");
        sb.AppendLine();
        sb.Append(tabelle);
        sb.AppendLine();
        sb.Append(abschnitte);

        return Ok(new
        {
            monate = zeilen,
            zusammenfassung = new { gesamt = quellen.Count, fertig, mitOffenen, nichtBereit },
            uebergangen = uebergangen ?? new List<string>(),
            bericht = sb.ToString()
        });
    }

    /// <summary>Jahr und Monat aus einem Referenz-Dateinamen — 0/0, wenn keiner drinsteht.</summary>
    public static (int Jahr, int Monat) MonatAusDateiname(string dateiname)
    {
        // Beide Schreibweisen: RefXML_2024-12_MONTHLY.xml und RefXML_202411_MONTHLY.xml.
        var m = Regex.Match(dateiname ?? "", @"(\d{4})-?(\d{2})_MONTHLY", RegexOptions.IgnoreCase);
        if (!m.Success) return (0, 0);
        var jahr = int.Parse(m.Groups[1].Value);
        var monat = int.Parse(m.Groups[2].Value);
        if (jahr < 2000 || jahr > 2100 || monat < 1 || monat > 12) return (0, 0);
        return (jahr, monat);
    }

    // ── Datei herunterladen ohne JavaScript-Kunststuecke (Walter 27.09.2026) ──
    //
    // Walters Browser scheiterte am ueblichen Weg (unsichtbaren Link in die Seite
    // haengen, anklicken): eine Erweiterung haengt sich an jede Einfuegung und wirft
    // «Failed to execute 'insertAdjacentHTML'». Statt weiter gegen den Browser zu
    // kaempfen, liefert der Server die Datei jetzt als gewoehnlichen Download aus.
    //
    // Weil ein gewoehnlicher Download eine normale Navigation ist, kann der Browser
    // dabei KEINEN Authorization-Kopf mitschicken. Darum zuerst eine Marke loesen
    // (angemeldet, POST) und dann mit dieser Marke abholen: zufaellig, EINMALIG,
    // zwei Minuten gueltig, an den abholenden Benutzer gebunden.

    private const string MarkePrefix = "elm-datei:";

    private sealed record DateiMarke(string Dateiname, byte[] Inhalt, string? UserId);

    /// <summary>Marke loesen: erzeugt die Meldung und legt sie fuer zwei Minuten bereit.</summary>
    [Authorize(Roles = "admin,superuser")]
    [HttpPost("monthly/{year:int}/{month:int}/datei-marke")]
    public async Task<IActionResult> DateiMarkeLoesen(int year, int month, CancellationToken ct)
    {
        if (year < 2020 || year > 2100 || month < 1 || month > 12)
            return BadRequest(new { error = "PERIODE_UNGUELTIG", message = "Bitte Jahr und Monat gültig angeben." });

        var r = await _monatsBuilder.BuildAsync(year, month, ct);
        if (r.Xml.Length == 0)
            return BadRequest(new { error = "KEINE_MELDUNG",
                message = r.Warnungen.FirstOrDefault() ?? "Für diesen Monat entsteht keine Meldung." });

        var marke = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var name = $"DeclareMonthlySalary_{year}-{month:00}.xml";
        _cache.Set(MarkePrefix + marke,
            new DateiMarke(name, new UTF8Encoding(false).GetBytes(r.Xml),
                           User.FindFirst(ClaimTypes.NameIdentifier)?.Value),
            TimeSpan.FromMinutes(2));

        return Ok(new { marke, dateiname = name, bytes = r.Xml.Length });
    }

    /// <summary>
    /// Die Datei abholen. Ohne Anmeldekopf — die Marke IST der Ausweis: zufaellig,
    /// nur zwei Minuten gueltig und nach dem ersten Abholen weg.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("datei/{marke}")]
    public IActionResult DateiAbholen(string marke)
    {
        var schluessel = MarkePrefix + (marke ?? "");
        if (!_cache.TryGetValue(schluessel, out DateiMarke? eintrag) || eintrag == null)
            return NotFound("Diese Marke gilt nicht mehr. Bitte die Meldung neu erzeugen und nochmals herunterladen.");
        _cache.Remove(schluessel);   // einmalig
        return File(eintrag.Inhalt, "application/xml", eintrag.Dateiname);
    }

    /// <summary>Monate, zu denen eine Monats-Referenz im Repo liegt — aufsteigend.</summary>
    private static List<(int Jahr, int Monat, string Datei)> ReferenzMonate()
    {
        var res = new List<(int, int, string)>();
        var ordner = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "SWISSCEC", "RefXML");
        if (!Directory.Exists(ordner)) return res;
        foreach (var datei in Directory.GetFiles(ordner, "RefXML_*MONTHLY*.xml"))
        {
            // Beide Schreibweisen im Ordner: RefXML_2024-12_MONTHLY und RefXML_202411_MONTHLY.
            var m = Regex.Match(System.IO.Path.GetFileName(datei), @"^RefXML_(\d{4})-?(\d{2})_MONTHLY");
            if (!m.Success) continue;
            res.Add((int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), datei));
        }
        return res
            .GroupBy(x => (x.Item1, x.Item2))          // pro Monat nur EINE Referenz
            .Select(g => g.OrderBy(x => x.Item3, StringComparer.Ordinal).First())
            .OrderBy(x => x.Item1).ThenBy(x => x.Item2).ToList();
    }

    // ── MonitoringID (Walter 27.09.2026) ──────────────────────────────
    // Auf den Swissdec-Testsystemen zwingend, auf der Produktion muss sie LEER
    // bleiben. Darum nur auf der Testinstanz editierbar.

    private static bool IstTestinstanz()
        => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("INSTANCE_LABEL"));

    public record MonitoringDto(string? MonitoringId);

    [HttpGet("monitoring-id")]
    public IActionResult MonitoringIdLesen()
        => Ok(new
        {
            gespeichert = _einstellungen.MonitoringIdGespeichert,
            ausServerVariable = _einstellungen.MonitoringIdAusKonfiguration,
            wirksam = _einstellungen.MonitoringId,
            editierbar = IstTestinstanz(),
            maxLaenge = ElmEinstellungen.MaxLaenge,
        });

    [HttpPut("monitoring-id")]
    public async Task<IActionResult> MonitoringIdSpeichern([FromBody] MonitoringDto dto, CancellationToken ct)
    {
        if (!IstTestinstanz())
            return StatusCode(403, new { error = "NUR_TESTINSTANZ",
                message = "Die MonitoringID gehört auf die Testsysteme. Auf der Produktion muss sie leer bleiben — dort wird sie nicht gebraucht." });

        var wert = ElmEinstellungen.Bereinige(dto.MonitoringId);
        var zeile = await _db.AppSettings.FirstOrDefaultAsync(x => x.Key == ElmEinstellungen.SettingKey, ct);
        if (wert == null)
        {
            if (zeile != null) _db.AppSettings.Remove(zeile);
        }
        else
        {
            if (zeile == null)
            {
                zeile = new Models.AppSetting { Key = ElmEinstellungen.SettingKey };
                _db.AppSettings.Add(zeile);
            }
            zeile.Value = wert;
            zeile.UpdatedAt = DateTime.Now;
        }
        await _db.SaveChangesAsync(ct);
        _einstellungen.Verwerfen();
        return Ok(new { gespeichert = wert, wirksam = _einstellungen.MonitoringId });
    }

    /// <summary>Referenz-XML von Swissdec, falls im Repo vorhanden (zwei Schreibweisen).</summary>
    private static string? ReferenzDatei(int year, int month)
    {
        var ordner = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "SWISSCEC", "RefXML");
        if (!Directory.Exists(ordner)) return null;
        foreach (var muster in new[] { $"RefXML_{year}-{month:00}_MONTHLY*.xml", $"RefXML_{year}{month:00}_MONTHLY*.xml" })
        {
            var treffer = Directory.GetFiles(ordner, muster).OrderBy(x => x).FirstOrDefault();
            if (treffer != null) return treffer;
        }
        return null;
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

    // ── Foundation F07: SUA-Zertifikat (Walter/Cursor 24.09.2026) ─────────

    [HttpGet("sua/status")]
    public async Task<IActionResult> SuaStatus(CancellationToken ct)
    {
        if (!await IstSuperAdminAsync()) return NurSuperAdmin();
        return Ok(await _sua.StatusAsync(ct));
    }

    [HttpPost("sua/erp-erzeugen")]
    public async Task<IActionResult> SuaErpErzeugen()
    {
        if (!await IstSuperAdminAsync()) return NurSuperAdmin();
        try { return Ok(_sua.ErzeugeErp()); }
        catch (Exception ex)
        {
            return BadRequest(new { error = "ERP_FEHLER", message = ex.GetBaseException().Message });
        }
    }

    /// <summary>
    /// Swissdec-TX-.pfx importieren (Foundation: selbst signiertes ERP reicht
    /// gegen RefApps nicht — Fault 100). Multipart: datei + optional passwort.
    /// </summary>
    [HttpPost("sua/erp-pfx")]
    public async Task<IActionResult> SuaErpPfxImport(IFormFile? datei, [FromForm] string? passwort)
    {
        if (!await IstSuperAdminAsync()) return NurSuperAdmin();
        if (datei == null || datei.Length == 0)
            return BadRequest(new { error = "DATEI_FEHLT", message = "Bitte die von Swissdec gelieferte .pfx wählen." });
        await using var ms = new MemoryStream();
        await datei.CopyToAsync(ms);
        try { return Ok(_sua.ImportiereErpPfx(ms.ToArray(), passwort)); }
        catch (Exception ex)
        {
            return BadRequest(new { error = "PFX_IMPORT", message = ex.GetBaseException().Message });
        }
    }

    public record ElmSuaRegisterBody(
        string? Ziel, string? Url,
        string? Uid, string? CompanyName, string? ContactName,
        string? Zip, string? City, string? AddresseeIdentification,
        string? Domain, string? InsuranceName, string? CustomerIdentity, string? ContractIdentity,
        // Standard bewusst FALSE: Ein Testfall lässt sich laut Richtlinie (Anhang C.2.1.2)
        // «starten, jedoch nicht abschliessen» — er liefert NIE ein Zertifikat.
        bool AlsTestfall = false);

    [HttpPost("sua/register")]
    public async Task<IActionResult> SuaRegister([FromBody] ElmSuaRegisterBody dto, CancellationToken ct)
    {
        if (!await IstSuperAdminAsync()) return NurSuperAdmin();
        var ziel = ZielAufloesen(new ElmZielDto(dto.Ziel, dto.Url));
        if (ziel == null)
            return BadRequest(new { error = "ZIEL_UNBEKANNT",
                message = "Bitte «test» oder «prod» wählen oder eine gültige https-Adresse eingeben." });
        try
        {
            var r = await _sua.RegisterAsync(ziel.Value.Url!, new ElmSuaRegisterDto(
                dto.Uid, dto.CompanyName, dto.ContactName, dto.Zip, dto.City,
                dto.AddresseeIdentification, dto.Domain, dto.InsuranceName, dto.CustomerIdentity,
                dto.ContractIdentity, dto.AlsTestfall), ct);
            return Ok(new { name = ziel.Value.Name, url = ziel.Value.Url,
                state = r.State, meldung = r.Meldung, fall = r.Fall, suaVorhanden = r.SuaVorhanden,
                // Was die Antwort inhaltlich sagt — Code, DescriptionCode, Description
                // (Walter 28.09.2026). Ein Einmalpasswort aus Code 9998 kommt als
                // Vorschlag mit, damit es niemand aus dem Roh-XML abtippen muss.
                meldungen = r.Meldungen?.Zeilen,
                code = r.Meldungen?.Code,
                descriptionCode = r.Meldungen?.DescriptionCode,
                description = r.Meldungen?.Description,
                otpVorschlag = r.Meldungen?.Einmalpasswort,
                abgewiesen = r.Meldungen?.Abgewiesen == true,
                ergebnis = r.Call });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = "SUA_REGISTER", message = ex.GetBaseException().Message });
        }
    }

    public record ElmSuaSyncBody(string? Ziel, string? Url, string? OneTimePassword, bool Renew = false);

    [HttpPost("sua/synchronize")]
    public async Task<IActionResult> SuaSynchronize([FromBody] ElmSuaSyncBody dto, CancellationToken ct)
    {
        if (!await IstSuperAdminAsync()) return NurSuperAdmin();
        var ziel = ZielAufloesen(new ElmZielDto(dto.Ziel, dto.Url));
        if (ziel == null)
            return BadRequest(new { error = "ZIEL_UNBEKANNT",
                message = "Bitte «test» oder «prod» wählen oder eine gültige https-Adresse eingeben." });
        try
        {
            var r = await _sua.SynchronizeAsync(ziel.Value.Url!, dto.OneTimePassword, dto.Renew, ct);
            return Ok(new { name = ziel.Value.Name, url = ziel.Value.Url,
                state = r.State, meldung = r.Meldung, fall = r.Fall, suaVorhanden = r.SuaVorhanden,
                // Was die Antwort inhaltlich sagt — Code, DescriptionCode, Description
                // (Walter 28.09.2026). Ein Einmalpasswort aus Code 9998 kommt als
                // Vorschlag mit, damit es niemand aus dem Roh-XML abtippen muss.
                meldungen = r.Meldungen?.Zeilen,
                code = r.Meldungen?.Code,
                descriptionCode = r.Meldungen?.DescriptionCode,
                description = r.Meldungen?.Description,
                otpVorschlag = r.Meldungen?.Einmalpasswort,
                abgewiesen = r.Meldungen?.Abgewiesen == true,
                ergebnis = r.Call });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = "SUA_SYNC", message = ex.GetBaseException().Message });
        }
    }

    [HttpPost("sua/empfaenger-zertifikat")]
    public async Task<IActionResult> SuaEmpfaengerZertifikat(IFormFile? datei)
    {
        if (!await IstSuperAdminAsync()) return NurSuperAdmin();
        if (datei == null || datei.Length == 0)
            return BadRequest(new { error = "DATEI_FEHLT", message = "Bitte eine .cer / .pem Datei wählen." });
        await using var ms = new MemoryStream();
        await datei.CopyToAsync(ms);
        _store.SpeichereEmpfaenger(ms.ToArray());
        return Ok(new { ok = true, message = "Empfängerzertifikat gespeichert — ab jetzt werden Requests verschlüsselt." });
    }

    // ── Kommunikations-Test: Stand je Foundation-Prüfpunkt (Walter 29.09.2026) ──

    [HttpGet("foundation/stand")]
    public async Task<IActionResult> FoundationStand()
    {
        if (!await IstSuperAdminAsync()) return NurSuperAdmin();
        return Ok(_store.LadeFoundationStand());
    }

    public record ElmFoundationDto(string? Status, string? Notiz, string? LetzterVersuch);

    private static readonly Regex PruefpunktId = new(@"^F0[1-8]_[0-9]{2}$");

    [HttpPut("foundation/stand/{id}")]
    public async Task<IActionResult> FoundationStandSetzen(string id, [FromBody] ElmFoundationDto dto)
    {
        if (!await IstSuperAdminAsync()) return NurSuperAdmin();
        if (!PruefpunktId.IsMatch(id ?? ""))
            return BadRequest(new { error = "ID_UNGUELTIG", message = "Prüfpunkt-ID im Format F07_06 erwartet." });
        var alt = _store.LadeFoundationStand().GetValueOrDefault(id!) ?? new ElmFoundationEintrag();
        var status = (dto.Status ?? alt.Status).Trim().ToLowerInvariant();
        if (status is not ("ok" or "fehler" or "offen"))
            return BadRequest(new { error = "STATUS_UNGUELTIG", message = "Status «ok», «fehler» oder «offen»." });
        alt.Status = status;
        if (dto.Notiz != null) alt.Notiz = dto.Notiz.Trim().Length == 0 ? null : dto.Notiz.Trim();
        if (!string.IsNullOrWhiteSpace(dto.LetzterVersuch))
        {
            alt.LetzterVersuch = dto.LetzterVersuch.Trim();
            alt.LetzterVersuchAm = DateTime.Now;
        }
        alt.GeaendertAm = DateTime.Now;
        _store.SpeichereFoundationEintrag(id!, alt);
        return Ok(alt);
    }

    /// <summary>
    /// Bereitschafts-Check vor dem Foundation-Termin: Zertifikate, MonitoringID, Archiv,
    /// dann ein Ping und ein CheckInterop gegen das gewählte Ziel. Nur auf Knopfdruck.
    /// </summary>
    [HttpPost("foundation/bereitschaft")]
    public async Task<IActionResult> FoundationBereitschaft([FromBody] ElmZielDto dto, CancellationToken ct)
    {
        if (!await IstSuperAdminAsync()) return NurSuperAdmin();
        var jetzt = DateTime.Now;
        var ziel = ZielAufloesen(dto);
        var punkte = new List<ElmBereitschaft.Punkt> { ElmBereitschaft.Ziel(ziel?.Url) };

        X509Certificate2? erp = null, sua = null;
        try { erp = _store.LadeErp(); } catch { /* unten als fehlend gemeldet */ }
        try { sua = _store.LadeSua(); } catch { /* unten als fehlend gemeldet */ }
        var vertrauen = _store.LadeVertrauensliste();
        punkte.Add(ElmBereitschaft.Erp(erp, jetzt));
        punkte.Add(ElmBereitschaft.Sua(sua, jetzt));
        punkte.Add(ElmBereitschaft.Empfaenger(_store.LadeEmpfaengerFuerVerschluesselung(), vertrauen));
        punkte.Add(ElmBereitschaft.Vertrauen(vertrauen));
        punkte.Add(ElmBereitschaft.Monitoring(_einstellungen.MonitoringId));
        var (anzahl, beschreibbar) = _store.ArchivZustand();
        punkte.Add(ElmBereitschaft.Archiv(anzahl, beschreibbar));

        if (ziel != null)
        {
            punkte.Add(ElmBereitschaft.Ping(await _client.PingAsync(ziel.Value.Url!, 0, ct)));
            if (erp?.HasPrivateKey == true)
                punkte.Add(ElmBereitschaft.Interop(
                    await _client.CheckInteroperabilityAsync(ziel.Value.Url!, 0.01m, 0, ct), sua?.HasPrivateKey == true));
            else
                punkte.Add(new ElmBereitschaft.Punkt("CheckInterop", ElmBereitschaft.Rot,
                    "Nicht versucht — ohne ERP-Zertifikat mit Schlüssel geht die Anfrage unsigniert raus."));
        }

        return Ok(new
        {
            geprueftAm = jetzt,
            gesamt = punkte.Any(p => p.Stufe == ElmBereitschaft.Rot) ? ElmBereitschaft.Rot
                   : punkte.Any(p => p.Stufe == ElmBereitschaft.Warn) ? ElmBereitschaft.Warn : ElmBereitschaft.Ok,
            punkte = punkte.Select(p => new { p.Titel, p.Stufe, p.Text, p.Tipp }),
        });
    }

    /// <summary>Foundation F04: archivierte Klartext-Nachrichten (signiert, unverschlüsselt).</summary>
    [HttpGet("archiv")]
    public async Task<IActionResult> Archiv()
    {
        if (!await IstSuperAdminAsync()) return NurSuperAdmin();
        return Ok(_store.ListeArchiv());
    }

    [HttpGet("archiv/{name}")]
    public async Task<IActionResult> ArchivDatei(string name)
    {
        if (!await IstSuperAdminAsync()) return NurSuperAdmin();
        var inhalt = _store.LeseArchiv(name);
        return inhalt == null
            ? NotFound(new { error = "NICHT_GEFUNDEN", message = "Archivdatei nicht gefunden." })
            : Content(inhalt, "application/xml; charset=utf-8");
    }
}
