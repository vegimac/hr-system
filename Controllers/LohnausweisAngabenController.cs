using System.Globalization;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Lohnausweis-Angaben für die Swissdec-Erfassungsmaske.
/// Person: eigene Werte, sonst die Vorgabe der Filiale.
/// Filiale: das, was der Basistest «ganzes Unternehmen» nennt
/// (gleicher Ort wie Box F und G).
/// </summary>
[ApiController]
[Authorize]
public class LohnausweisAngabenController : ControllerBase
{
    private readonly AppDbContext _db;

    public LohnausweisAngabenController(AppDbContext db) => _db = db;

    [HttpGet("api/employees/{employeeId:int}/lohnausweis-angaben")]
    public async Task<IActionResult> PersonLesen(int employeeId)
    {
        var emp = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId);
        if (emp == null) return NotFound();

        var alle = await _db.EmployeeLohnausweise.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId)
            .OrderBy(x => x.GueltigAb)
            .ToListAsync();
        var offen = Offen(alle);
        var gezeigt = Ab(Request) is int ab
            ? alle.FirstOrDefault(x => x.GueltigAb == ab) ?? offen
            : offen;
        var firma = await FilialeDerPersonAsync(employeeId);
        var firmaFassung = firma == null ? null : await FirmaImJahrAsync(firma.Id, gezeigt?.GueltigAb ?? DateTime.Today.Year);
        if (firma != null && firmaFassung != null) LohnausweisFassung.AufProfil(firma, firmaFassung);
        var wirkung = LohnausweisAngaben.Aufloesen(firma, gezeigt, gezeigt?.GueltigAb ?? DateTime.Today.Year, null);

        return Ok(new
        {
            person = NachAussen(gezeigt),
            firma = FirmaNachAussen(firma, firmaFassung),
            firmaName = firma?.BranchName ?? firma?.CompanyName,
            hinweis = wirkung.SpesenHinweis,
            staende = alle.Select(x => new { ab = x.GueltigAb, bis = x.GueltigBis }),
            geschlossen = gezeigt?.GueltigBis != null,
        });
    }

    [HttpPut("api/employees/{employeeId:int}/lohnausweis-angaben")]
    public async Task<IActionResult> PersonSpeichern(int employeeId, [FromBody] PersonDto dto)
    {
        if (!await _db.Employees.AnyAsync(e => e.Id == employeeId)) return NotFound();
        if (dto == null) return BadRequest(new { message = "Keine Daten erhalten." });
        if (PruefePerson(dto) is string fehler)
            return BadRequest(new { message = fehler });

        var alle = await _db.EmployeeLohnausweise
            .Where(x => x.EmployeeId == employeeId)
            .ToListAsync();
        var offen = Offen(alle);
        var jahr = dto.GueltigAb ?? offen?.GueltigAb ?? LohnausweisFassung.Beginn;
        int? bisherBis = null;
        if (offen != null && LohnausweisFassung.Entscheiden(offen.GueltigAb, jahr, out bisherBis) is string hinweis)
            return BadRequest(new { message = hinweis });

        EmployeeLohnausweis zeile;
        if (offen == null)
        {
            zeile = new EmployeeLohnausweis { EmployeeId = employeeId, GueltigAb = jahr };
            _db.EmployeeLohnausweise.Add(zeile);
        }
        else if (bisherBis != null)
        {
            offen.GueltigBis = bisherBis;
            offen.GeaendertAm = DateTime.Now;
            zeile = new EmployeeLohnausweis { EmployeeId = employeeId, GueltigAb = jahr };
            _db.EmployeeLohnausweise.Add(zeile);
        }
        else zeile = offen;

        zeile.Art = NormArt(dto.Art);
        zeile.BoxF = dto.BoxF;
        zeile.BoxG = dto.BoxG;
        zeile.Ziffer14Modus = NormModus(dto.Ziffer14Modus);
        zeile.Ziffer14Text = Leer(dto.Ziffer14Text);
        zeile.ExpatModus = NormModus(dto.ExpatModus);
        zeile.ExpatDatum = dto.ExpatDatum;
        zeile.ExpatKanton = Kanton(dto.ExpatKanton);
        zeile.WagenAbklaeren = dto.WagenAbklaeren;
        zeile.WagenMinimal = dto.WagenMinimal;
        zeile.SpesenModus = NormModus(dto.SpesenModus);
        zeile.SpesenDatum = dto.SpesenDatum;
        zeile.SpesenKanton = Kanton(dto.SpesenKanton);
        zeile.Rz52 = dto.Rz52;
        zeile.WertModus = NormModus(dto.WertModus);
        zeile.WertDatum = dto.WertDatum;
        zeile.WertKanton = Kanton(dto.WertKanton);
        zeile.BeteiligungModus = NormModus(dto.BeteiligungModus);
        zeile.BeteiligungGesperrt = dto.BeteiligungGesperrt;
        zeile.BeteiligungNichtKotiert = dto.BeteiligungNichtKotiert;
        zeile.BeteiligungAnwartschaft = dto.BeteiligungAnwartschaft;
        zeile.BeteiligungUnecht = dto.BeteiligungUnecht;
        zeile.KinderzulageAhv = dto.KinderzulageAhv;
        zeile.UmzugBetrag = dto.UmzugBetrag;
        zeile.UmzugJahr = dto.UmzugJahr;
        zeile.Nachgenuss = dto.Nachgenuss;
        zeile.NachgenussBetrag = dto.NachgenussBetrag;
        zeile.NachgenussJahr = dto.NachgenussJahr;
        zeile.NachgenussName = Leer(dto.NachgenussName);
        zeile.NachgenussVorname = Leer(dto.NachgenussVorname);
        zeile.NachgenussStrasse = Leer(dto.NachgenussStrasse);
        zeile.NachgenussPlz = Leer(dto.NachgenussPlz);
        zeile.NachgenussOrt = Leer(dto.NachgenussOrt);
        zeile.KontaktModus = NormModus(dto.KontaktModus);
        zeile.KontaktOrg = Leer(dto.KontaktOrg);
        zeile.KontaktStrasse = Leer(dto.KontaktStrasse);
        zeile.KontaktPlz = Leer(dto.KontaktPlz);
        zeile.KontaktOrt = Leer(dto.KontaktOrt);
        zeile.KontaktPerson = Leer(dto.KontaktPerson);
        zeile.KontaktTelefon = Leer(dto.KontaktTelefon);
        zeile.GeaendertAm = DateTime.Now;

        await _db.SaveChangesAsync();
        return Ok(NachAussen(zeile));
    }

    [HttpGet("api/companyprofiles/{id:int}/lohnausweis-angaben")]
    public async Task<IActionResult> FirmaLesen(int id)
    {
        var firma = await _db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        if (firma == null) return NotFound();
        var alle = await _db.CompanyLohnausweise.AsNoTracking()
            .Where(x => x.CompanyProfileId == id)
            .OrderBy(x => x.GueltigAb)
            .ToListAsync();
        var offen = Offen(alle);
        var gezeigt = Ab(Request) is int ab
            ? alle.FirstOrDefault(x => x.GueltigAb == ab) ?? offen
            : offen;
        if (gezeigt != null) LohnausweisFassung.AufProfil(firma, gezeigt);
        else gezeigt = LohnausweisFassung.AusProfil(firma, LohnausweisFassung.Beginn);
        return Ok(FirmaAntwort(firma, gezeigt, alle, gezeigt.GueltigBis != null && alle.Contains(gezeigt)));
    }

    [Authorize(Roles = "admin")]
    [HttpPut("api/companyprofiles/{id:int}/lohnausweis-angaben")]
    public async Task<IActionResult> FirmaSpeichern(int id, [FromBody] FirmaDto dto)
    {
        var firma = await _db.CompanyProfiles.FirstOrDefaultAsync(c => c.Id == id);
        if (firma == null) return NotFound();
        if (dto == null) return BadRequest(new { message = "Keine Daten erhalten." });
        if (PruefeFirma(dto) is string fehler)
            return BadRequest(new { message = fehler });

        var alle = await _db.CompanyLohnausweise
            .Where(x => x.CompanyProfileId == id)
            .ToListAsync();
        var offen = Offen(alle);
        var jahr = dto.GueltigAb ?? offen?.GueltigAb ?? LohnausweisFassung.Beginn;
        int? bisherBis = null;
        if (offen != null && LohnausweisFassung.Entscheiden(offen.GueltigAb, jahr, out bisherBis) is string hinweis)
            return BadRequest(new { message = hinweis });

        CompanyLohnausweis zeile;
        if (offen == null)
        {
            zeile = new CompanyLohnausweis { CompanyProfileId = id, GueltigAb = jahr };
            _db.CompanyLohnausweise.Add(zeile);
        }
        else if (bisherBis != null)
        {
            offen.GueltigBis = bisherBis;
            offen.GeaendertAm = DateTime.Now;
            zeile = new CompanyLohnausweis { CompanyProfileId = id, GueltigAb = jahr };
            _db.CompanyLohnausweise.Add(zeile);
        }
        else zeile = offen;

        SchreibFirma(zeile, dto);
        zeile.GeaendertAm = DateTime.Now;
        LohnausweisFassung.AufProfil(firma, zeile.GueltigBis == null ? zeile : offen!);

        await _db.SaveChangesAsync();
        return Ok(FirmaAntwort(firma, zeile, alle.Append(zeile).Distinct(), false));
    }

    [Authorize(Roles = "admin")]
    [HttpDelete("api/companyprofiles/{id:int}/lohnausweis-angaben")]
    public async Task<IActionResult> FirmaLoeschen(int id)
    {
        var firma = await _db.CompanyProfiles.FirstOrDefaultAsync(c => c.Id == id);
        if (firma == null) return NotFound();
        var alle = await _db.CompanyLohnausweise
            .Where(x => x.CompanyProfileId == id)
            .ToListAsync();
        var offen = alle.Where(x => x.GueltigBis == null).OrderByDescending(x => x.GueltigAb).FirstOrDefault();
        if (offen == null)
            return BadRequest(new { message = "Nur die offene Fassung kann gelöscht werden." });
        var vorher = LohnausweisFassung.Vorversion(alle, offen.GueltigAb, x => x.GueltigAb);
        if (vorher == null)
            return BadRequest(new { message = "Die einzige Fassung bleibt." });

        _db.CompanyLohnausweise.Remove(offen);
        alle.Remove(offen);
        vorher.GueltigBis = null;
        vorher.GeaendertAm = DateTime.Now;
        LohnausweisFassung.AufProfil(firma, vorher);
        await _db.SaveChangesAsync();
        return Ok(FirmaAntwort(firma, vorher, alle, false));
    }

    private async Task<CompanyProfile?> FilialeDerPersonAsync(int employeeId)
    {
        var vertraege = await _db.Employments.AsNoTracking()
            .Where(e => e.EmployeeId == employeeId && e.CompanyProfileId != null)
            .Select(e => new { e.CompanyProfileId, e.ContractStartDate, e.ContractEndDate })
            .ToListAsync();
        var heute = DateTime.Today;
        var gewaehlt = vertraege
            .OrderByDescending(e => e.ContractEndDate == null || e.ContractEndDate >= heute)
            .ThenByDescending(e => e.ContractStartDate)
            .FirstOrDefault();
        if (gewaehlt?.CompanyProfileId is not int id) return null;
        return await _db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
    }

    private static object NachAussen(EmployeeLohnausweis? z) => new
    {
        gueltigAb = z?.GueltigAb ?? LohnausweisFassung.Beginn,
        gueltigBis = z?.GueltigBis,
        art = z?.Art ?? LohnausweisAngaben.ArtLohn,
        boxF = z?.BoxF,
        boxG = z?.BoxG,
        ziffer14Modus = z?.Ziffer14Modus,
        ziffer14Text = z?.Ziffer14Text,
        expatModus = z?.ExpatModus,
        expatDatum = Iso(z?.ExpatDatum),
        expatKanton = z?.ExpatKanton,
        wagenAbklaeren = z?.WagenAbklaeren,
        wagenMinimal = z?.WagenMinimal,
        spesenModus = z?.SpesenModus,
        spesenDatum = Iso(z?.SpesenDatum),
        spesenKanton = z?.SpesenKanton,
        rz52 = z?.Rz52,
        wertModus = z?.WertModus,
        wertDatum = Iso(z?.WertDatum),
        wertKanton = z?.WertKanton,
        beteiligungModus = z?.BeteiligungModus,
        beteiligungGesperrt = z?.BeteiligungGesperrt ?? false,
        beteiligungNichtKotiert = z?.BeteiligungNichtKotiert ?? false,
        beteiligungAnwartschaft = z?.BeteiligungAnwartschaft ?? false,
        beteiligungUnecht = z?.BeteiligungUnecht ?? false,
        kinderzulageAhv = z?.KinderzulageAhv ?? false,
        umzugBetrag = z?.UmzugBetrag,
        umzugJahr = z?.UmzugJahr,
        nachgenuss = z?.Nachgenuss ?? false,
        nachgenussBetrag = z?.NachgenussBetrag,
        nachgenussJahr = z?.NachgenussJahr,
        nachgenussName = z?.NachgenussName,
        nachgenussVorname = z?.NachgenussVorname,
        nachgenussStrasse = z?.NachgenussStrasse,
        nachgenussPlz = z?.NachgenussPlz,
        nachgenussOrt = z?.NachgenussOrt,
        kontaktModus = z?.KontaktModus,
        kontaktOrg = z?.KontaktOrg,
        kontaktStrasse = z?.KontaktStrasse,
        kontaktPlz = z?.KontaktPlz,
        kontaktOrt = z?.KontaktOrt,
        kontaktPerson = z?.KontaktPerson,
        kontaktTelefon = z?.KontaktTelefon,
    };

    private static object? FirmaNachAussen(CompanyProfile? f, CompanyLohnausweis? v = null)
    {
        if (f == null && v == null) return null;
        return new
        {
            id = f?.Id ?? v!.CompanyProfileId,
            name = f?.BranchName ?? f?.CompanyName,
            gueltigAb = v?.GueltigAb ?? LohnausweisFassung.Beginn,
            gueltigBis = v?.GueltigBis,
            boxF = v?.BoxF ?? f?.LohnausweisBoxFFreierTransport ?? false,
            boxG = v?.BoxG ?? f?.LohnausweisBoxGKantineGratis ?? false,
            pos21 = v?.Pos21 ?? f?.LohnausweisPos21VerpflegungMonat,
            ziffer14 = v?.Ziffer14 ?? f?.LohnausweisZiffer14,
            expatDatum = Iso(v?.ExpatDatum ?? f?.LohnausweisExpatDatum),
            expatKanton = v?.ExpatKanton ?? f?.LohnausweisExpatKanton,
            wagenAbklaeren = v?.WagenAbklaeren ?? f?.LohnausweisWagenAbklaeren ?? false,
            wagenMinimal = v?.WagenMinimal ?? f?.LohnausweisWagenMinimal ?? false,
            spesenDatum = Iso(v?.SpesenDatum ?? f?.LohnausweisSpesenDatum),
            spesenKanton = v?.SpesenKanton ?? f?.LohnausweisSpesenKanton,
            rz52 = v?.Rz52 ?? f?.LohnausweisRz52 ?? false,
            wertDatum = Iso(v?.WertDatum ?? f?.LohnausweisWertDatum),
            wertKanton = v?.WertKanton ?? f?.LohnausweisWertKanton,
            betGesperrt = v?.BetGesperrt ?? f?.LohnausweisBetGesperrt ?? false,
            betNichtKotiert = v?.BetNichtKotiert ?? f?.LohnausweisBetNichtKotiert ?? false,
            betAnwartschaft = v?.BetAnwartschaft ?? f?.LohnausweisBetAnwartschaft ?? false,
            betUnecht = v?.BetUnecht ?? f?.LohnausweisBetUnecht ?? false,
            kontaktOrg = v?.KontaktOrg ?? f?.LohnausweisKontaktOrg,
            kontaktStrasse = v?.KontaktStrasse ?? f?.LohnausweisKontaktStrasse,
            kontaktPlz = v?.KontaktPlz ?? f?.LohnausweisKontaktPlz,
            kontaktOrt = v?.KontaktOrt ?? f?.LohnausweisKontaktOrt,
            kontaktPerson = v?.KontaktPerson ?? f?.LohnausweisKontaktPerson,
            kontaktTelefon = v?.KontaktTelefon ?? f?.LohnausweisKontaktTelefon,
        };
    }

    private object FirmaAntwort(CompanyProfile firma, CompanyLohnausweis gezeigt, IEnumerable<CompanyLohnausweis> alle, bool geschlossen) => new
    {
        id = firma.Id,
        name = firma.BranchName ?? firma.CompanyName,
        gueltigAb = gezeigt.GueltigAb,
        gueltigBis = gezeigt.GueltigBis,
        boxF = gezeigt.BoxF,
        boxG = gezeigt.BoxG,
        pos21 = gezeigt.Pos21,
        ziffer14 = gezeigt.Ziffer14,
        expatDatum = Iso(gezeigt.ExpatDatum),
        expatKanton = gezeigt.ExpatKanton,
        wagenAbklaeren = gezeigt.WagenAbklaeren,
        wagenMinimal = gezeigt.WagenMinimal,
        spesenDatum = Iso(gezeigt.SpesenDatum),
        spesenKanton = gezeigt.SpesenKanton,
        rz52 = gezeigt.Rz52,
        wertDatum = Iso(gezeigt.WertDatum),
        wertKanton = gezeigt.WertKanton,
        betGesperrt = gezeigt.BetGesperrt,
        betNichtKotiert = gezeigt.BetNichtKotiert,
        betAnwartschaft = gezeigt.BetAnwartschaft,
        betUnecht = gezeigt.BetUnecht,
        kontaktOrg = gezeigt.KontaktOrg,
        kontaktStrasse = gezeigt.KontaktStrasse,
        kontaktPlz = gezeigt.KontaktPlz,
        kontaktOrt = gezeigt.KontaktOrt,
        kontaktPerson = gezeigt.KontaktPerson,
        kontaktTelefon = gezeigt.KontaktTelefon,
        staende = alle.Select(x => new { ab = x.GueltigAb, bis = x.GueltigBis }).Distinct(),
        fassungen = alle.OrderByDescending(x => x.GueltigAb).Select(x => FirmaNachAussen(firma, x)).ToList(),
        geschlossen,
    };

    private async Task<CompanyLohnausweis?> FirmaImJahrAsync(int companyId, int jahr)
    {
        var alle = await _db.CompanyLohnausweise.AsNoTracking()
            .Where(x => x.CompanyProfileId == companyId)
            .ToListAsync();
        return LohnausweisFassung.FuerJahr(alle, jahr, x => x.GueltigAb, x => x.GueltigBis);
    }

    private static EmployeeLohnausweis? Offen(IReadOnlyCollection<EmployeeLohnausweis> alle) =>
        alle.Where(x => x.GueltigBis == null).OrderByDescending(x => x.GueltigAb).FirstOrDefault()
        ?? alle.OrderByDescending(x => x.GueltigAb).FirstOrDefault();

    private static CompanyLohnausweis? Offen(IReadOnlyCollection<CompanyLohnausweis> alle) =>
        alle.Where(x => x.GueltigBis == null).OrderByDescending(x => x.GueltigAb).FirstOrDefault()
        ?? alle.OrderByDescending(x => x.GueltigAb).FirstOrDefault();

    private static int? Ab(HttpRequest request)
    {
        var roh = request.Query["ab"].ToString();
        return int.TryParse(roh, NumberStyles.Integer, CultureInfo.InvariantCulture, out var jahr) ? jahr : null;
    }

    private static void SchreibFirma(CompanyLohnausweis zeile, FirmaDto dto)
    {
        zeile.BoxF = dto.BoxF;
        zeile.BoxG = dto.BoxG;
        zeile.Pos21 = dto.Pos21 is > 0 ? dto.Pos21 : null;
        zeile.Ziffer14 = Leer(dto.Ziffer14);
        zeile.ExpatDatum = dto.ExpatDatum;
        zeile.ExpatKanton = Kanton(dto.ExpatKanton);
        zeile.WagenAbklaeren = dto.WagenAbklaeren;
        zeile.WagenMinimal = dto.WagenMinimal;
        zeile.SpesenDatum = dto.SpesenDatum;
        zeile.SpesenKanton = Kanton(dto.SpesenKanton);
        zeile.Rz52 = dto.Rz52;
        zeile.WertDatum = dto.WertDatum;
        zeile.WertKanton = Kanton(dto.WertKanton);
        zeile.BetGesperrt = dto.BetGesperrt;
        zeile.BetNichtKotiert = dto.BetNichtKotiert;
        zeile.BetAnwartschaft = dto.BetAnwartschaft;
        zeile.BetUnecht = dto.BetUnecht;
        zeile.KontaktOrg = Leer(dto.KontaktOrg);
        zeile.KontaktStrasse = Leer(dto.KontaktStrasse);
        zeile.KontaktPlz = Leer(dto.KontaktPlz);
        zeile.KontaktOrt = Leer(dto.KontaktOrt);
        zeile.KontaktPerson = Leer(dto.KontaktPerson);
        zeile.KontaktTelefon = Leer(dto.KontaktTelefon);
    }

    private static string? PruefePerson(PersonDto dto)
    {
        if (NormArt(dto.Art) is not ("LOHN" or "RENTE"))
            return "Art muss Lohnausweis oder Rentenbescheinigung sein.";
        if (NormModus(dto.Ziffer14Modus) == LohnausweisAngaben.ModusEigen
            && string.IsNullOrWhiteSpace(dto.Ziffer14Text))
            return "Für den eigenen Text in Ziffer 14 braucht es einen Text.";
        if (GrantFehler(dto.ExpatModus, dto.ExpatDatum, dto.ExpatKanton, "Expatriate-Ruling") is string e1)
            return e1;
        if (GrantFehler(dto.SpesenModus, dto.SpesenDatum, dto.SpesenKanton, "Spesenreglement") is string e2)
            return e2;
        if (GrantFehler(dto.WertModus, dto.WertDatum, dto.WertKanton, "Verkehrswert") is string e3)
            return e3;
        var betModus = NormModus(dto.BeteiligungModus);
        if (betModus == LohnausweisAngaben.ModusEigen
            && !dto.BeteiligungGesperrt && !dto.BeteiligungNichtKotiert
            && !dto.BeteiligungAnwartschaft && !dto.BeteiligungUnecht)
            return "Bei Mitarbeiterbeteiligungen ohne steuerbares Einkommen mindestens einen Grund wählen.";
        if (dto.UmzugBetrag is decimal u)
        {
            if (u <= 0) return "Der Umzugsbetrag muss grösser als 0 sein.";
            if (dto.UmzugJahr is not (>= 2000 and <= 2100))
                return "Zum Umzugsbetrag gehört das Kalenderjahr des Lohnausweises.";
        }
        if (dto.Nachgenuss)
        {
            if (dto.NachgenussBetrag is not decimal b || b <= 0)
                return "Zum Besoldungsnachgenuss gehört ein Betrag.";
            if (dto.NachgenussJahr is not (>= 2000 and <= 2100))
                return "Zum Besoldungsnachgenuss gehört das Kalenderjahr.";
            if (string.IsNullOrWhiteSpace(dto.NachgenussName)
                || string.IsNullOrWhiteSpace(dto.NachgenussVorname)
                || string.IsNullOrWhiteSpace(dto.NachgenussStrasse)
                || string.IsNullOrWhiteSpace(dto.NachgenussPlz)
                || string.IsNullOrWhiteSpace(dto.NachgenussOrt))
                return "Zur begünstigten Person gehören Name, Vorname und Adresse.";
        }
        if (NormModus(dto.KontaktModus) == LohnausweisAngaben.ModusEigen
            && KontaktFehler(dto.KontaktOrg, dto.KontaktStrasse, dto.KontaktPlz, dto.KontaktOrt,
                dto.KontaktPerson, dto.KontaktTelefon) is string ek)
            return ek;
        return null;
    }

    private static string? PruefeFirma(FirmaDto dto)
    {
        if (Paar(dto.ExpatDatum, dto.ExpatKanton, "Expatriate-Ruling") is string e1) return e1;
        if (Paar(dto.SpesenDatum, dto.SpesenKanton, "Spesenreglement") is string e2) return e2;
        if (Paar(dto.WertDatum, dto.WertKanton, "Verkehrswert") is string e3) return e3;
        var gruende = (dto.BetGesperrt ? 1 : 0) + (dto.BetNichtKotiert ? 1 : 0)
            + (dto.BetAnwartschaft ? 1 : 0) + (dto.BetUnecht ? 1 : 0);
        if (gruende == 0 && (dto.BetGesperrt || dto.BetNichtKotiert || dto.BetAnwartschaft || dto.BetUnecht))
            return null;
        var kontaktAngefangen = new[] { dto.KontaktOrg, dto.KontaktStrasse, dto.KontaktPlz, dto.KontaktOrt, dto.KontaktPerson, dto.KontaktTelefon }
            .Any(s => !string.IsNullOrWhiteSpace(s));
        if (kontaktAngefangen
            && KontaktFehler(dto.KontaktOrg, dto.KontaktStrasse, dto.KontaktPlz, dto.KontaktOrt,
                dto.KontaktPerson, dto.KontaktTelefon) is string ek)
            return ek;
        return null;
    }

    private static string? KontaktFehler(
        string? org, string? strasse, string? plz, string? ort, string? person, string? telefon)
    {
        if (string.IsNullOrWhiteSpace(org) || string.IsNullOrWhiteSpace(strasse)
            || string.IsNullOrWhiteSpace(plz) || string.IsNullOrWhiteSpace(ort)
            || string.IsNullOrWhiteSpace(person) || string.IsNullOrWhiteSpace(telefon))
            return "Die Kontaktperson braucht Organisation, Strasse, PLZ, Ort, Name und Telefon.";
        return null;
    }

    private static string? GrantFehler(string? modus, DateOnly? datum, string? kanton, string name)
    {
        if (NormModus(modus) != LohnausweisAngaben.ModusEigen) return null;
        return Paar(datum, kanton, name);
    }

    private static string? Paar(DateOnly? datum, string? kanton, string name)
    {
        var hatDatum = datum != null;
        var hatKanton = !string.IsNullOrWhiteSpace(kanton);
        if (!hatDatum && !hatKanton) return null;
        if (!hatDatum || !hatKanton)
            return name + " braucht immer Datum und Kanton.";
        if (!LohnausweisAngaben.IstKanton(kanton))
            return "Kanton «" + kanton + "» ist kein Schweizer Kanton.";
        return null;
    }

    private static string NormArt(string? art)
    {
        var a = (art ?? "").Trim().ToUpperInvariant();
        return a == LohnausweisAngaben.ArtRente ? LohnausweisAngaben.ArtRente : LohnausweisAngaben.ArtLohn;
    }

    private static string? NormModus(string? modus)
    {
        var m = (modus ?? "").Trim().ToUpperInvariant();
        return m is LohnausweisAngaben.ModusEigen or LohnausweisAngaben.ModusKein ? m : null;
    }

    private static string? Kanton(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();

    private static string? Leer(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? Iso(DateOnly? d) =>
        d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public class PersonDto
    {
        public int? GueltigAb { get; set; }
        public string? Art { get; set; }
        public bool? BoxF { get; set; }
        public bool? BoxG { get; set; }
        public string? Ziffer14Modus { get; set; }
        public string? Ziffer14Text { get; set; }
        public string? ExpatModus { get; set; }
        public DateOnly? ExpatDatum { get; set; }
        public string? ExpatKanton { get; set; }
        public bool? WagenAbklaeren { get; set; }
        public bool? WagenMinimal { get; set; }
        public string? SpesenModus { get; set; }
        public DateOnly? SpesenDatum { get; set; }
        public string? SpesenKanton { get; set; }
        public bool? Rz52 { get; set; }
        public string? WertModus { get; set; }
        public DateOnly? WertDatum { get; set; }
        public string? WertKanton { get; set; }
        public string? BeteiligungModus { get; set; }
        public bool BeteiligungGesperrt { get; set; }
        public bool BeteiligungNichtKotiert { get; set; }
        public bool BeteiligungAnwartschaft { get; set; }
        public bool BeteiligungUnecht { get; set; }
        public bool KinderzulageAhv { get; set; }
        public decimal? UmzugBetrag { get; set; }
        public int? UmzugJahr { get; set; }
        public bool Nachgenuss { get; set; }
        public decimal? NachgenussBetrag { get; set; }
        public int? NachgenussJahr { get; set; }
        public string? NachgenussName { get; set; }
        public string? NachgenussVorname { get; set; }
        public string? NachgenussStrasse { get; set; }
        public string? NachgenussPlz { get; set; }
        public string? NachgenussOrt { get; set; }
        public string? KontaktModus { get; set; }
        public string? KontaktOrg { get; set; }
        public string? KontaktStrasse { get; set; }
        public string? KontaktPlz { get; set; }
        public string? KontaktOrt { get; set; }
        public string? KontaktPerson { get; set; }
        public string? KontaktTelefon { get; set; }
    }

    public class FirmaDto
    {
        public int? GueltigAb { get; set; }
        public bool BoxF { get; set; }
        public bool BoxG { get; set; }
        public decimal? Pos21 { get; set; }
        public string? Ziffer14 { get; set; }
        public DateOnly? ExpatDatum { get; set; }
        public string? ExpatKanton { get; set; }
        public bool WagenAbklaeren { get; set; }
        public bool WagenMinimal { get; set; }
        public DateOnly? SpesenDatum { get; set; }
        public string? SpesenKanton { get; set; }
        public bool Rz52 { get; set; }
        public DateOnly? WertDatum { get; set; }
        public string? WertKanton { get; set; }
        public bool BetGesperrt { get; set; }
        public bool BetNichtKotiert { get; set; }
        public bool BetAnwartschaft { get; set; }
        public bool BetUnecht { get; set; }
        public string? KontaktOrg { get; set; }
        public string? KontaktStrasse { get; set; }
        public string? KontaktPlz { get; set; }
        public string? KontaktOrt { get; set; }
        public string? KontaktPerson { get; set; }
        public string? KontaktTelefon { get; set; }
    }
}
