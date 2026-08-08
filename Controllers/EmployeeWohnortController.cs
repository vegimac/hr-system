using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Wohnort-Historie + Umzugsdatum-Bestätigung (Walter-Vorgabe 07./08.08.2026).
///
/// Adressen werden AUSSCHLIESSLICH in easy@work gepflegt. Der Sync erkennt
/// PLZ-/Ort-Wechsel und legt einen Historie-Eintrag mit DatumOffen=true an.
/// POST umzug bestätigt NUR das Umzugsdatum zu diesem offenen Eintrag
/// (kein offener Wechsel → 409 KEIN_OFFENER_WECHSEL) und löst bei einem
/// Kantonswechsel die QST-Folge-Version aus: alter Kanton (= Steuerkanton
/// der aktiven QST-Version) bis Ende Umzugsmonat, neuer Kanton ab 1. des
/// FOLGEmonats — der angebrochene Monat zahlt im alten Kanton. Tarif/Kinder/
/// Kirchensteuer werden unverändert übernommen.
///
/// Admin-Korrekturen (PUT/DELETE): nur Gültig-ab-Datum + Bemerkung bzw.
/// Eintrag löschen — Adressdaten sind auch hier NICHT editierbar.
///
/// Lock: die QST-Folge-Version respektiert den Lohn-Edit-Lock (Soft-Lock wie
/// Verträge) — liegt der Folgemonat in einer verarbeiteten Periode → 409.
/// </summary>
[Authorize(Roles = "admin,superuser")]
[ApiController]
[Route("api/employees/{employeeId:int}/wohnort")]
public class EmployeeWohnortController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly LohnEditLockService _editLock;
    public EmployeeWohnortController(AppDbContext db, LohnEditLockService editLock)
    {
        _db = db; _editLock = editLock;
    }

    [HttpGet]
    public async Task<IActionResult> GetHistory(int employeeId)
    {
        var list = await _db.EmployeeWohnortHistories.AsNoTracking()
            .Where(h => h.EmployeeId == employeeId)
            .OrderBy(h => h.GueltigAb == null ? 0 : 1).ThenBy(h => h.GueltigAb)
            .ToListAsync();
        var result = new List<object>();
        for (int i = 0; i < list.Count; i++)
        {
            var h = list[i];
            DateOnly? bis = (i + 1 < list.Count && list[i + 1].GueltigAb.HasValue)
                ? list[i + 1].GueltigAb!.Value.AddDays(-1)
                : null;
            result.Add(new
            {
                h.Id, h.Plz, h.Ort, kantonCode = h.KantonCode,
                gueltigAb = h.GueltigAb?.ToString("yyyy-MM-dd"),
                gueltigBis = bis?.ToString("yyyy-MM-dd"),
                datumOffen = h.DatumOffen,
                h.Bemerkung,
            });
        }
        return Ok(result);
    }

    [HttpPost("umzug")]
    public async Task<IActionResult> Umzug(int employeeId, [FromBody] UmzugDto dto)
    {
        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId && !e.IsHidden);
        if (emp == null) return NotFound(new { error = "EMP_NOT_FOUND" });
        if (!DateOnly.TryParse(dto.Umzugsdatum, out var umzug))
            return BadRequest(new { error = "UMZUGSDATUM_UNGUELTIG" });

        // Adresse kommt AUSSCHLIESSLICH aus easy@work (Walter 08.08.2026):
        // hier wird nur noch das UMZUGSDATUM zum offenen Adresswechsel
        // bestätigt — ohne offenen Wechsel gibt es nichts zu erfassen.
        var offenerWechsel = await _db.EmployeeWohnortHistories
            .Where(h => h.EmployeeId == employeeId && h.DatumOffen)
            .OrderByDescending(h => h.Id)
            .FirstOrDefaultAsync();
        if (offenerWechsel == null)
            return Conflict(new
            {
                error = "KEIN_OFFENER_WECHSEL",
                message = "Kein offener Adresswechsel — Adresse zuerst in easy@work ändern und den MA synchronisieren.",
            });

        var neuerKanton = (offenerWechsel.KantonCode ?? "").Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(neuerKanton))
            neuerKanton = (emp.CantonCode ?? "").Trim().ToUpperInvariant();

        // Massgebender ALTER Kanton = Steuerkanton der aktiven QST-Version
        // (Walter 08.08.2026): robust, egal ob die MA-Adresse schon von easy
        // überschrieben wurde (Pending) oder der Umzug manuell kommt.
        var folgeMonatErster = new DateOnly(umzug.Year, umzug.Month, 1).AddMonths(1);
        var qstAlt = await _db.EmployeeQuellensteuer
            .Where(q => q.EmployeeId == employeeId
                     && q.ValidFrom < folgeMonatErster
                     && (q.ValidTo == null || q.ValidTo >= umzug))
            .OrderByDescending(q => q.ValidFrom)
            .FirstOrDefaultAsync();
        var alterKanton = (qstAlt?.Steuerkanton ?? emp.CantonCode ?? "").Trim().ToUpperInvariant();
        bool kantonswechsel = !string.IsNullOrEmpty(alterKanton) && alterKanton != neuerKanton;

        string? qstInfo = null;
        if (kantonswechsel)
        {
            if (qstAlt != null)
            {
                // Soft-Lock wie Verträge/QST-Versionen.
                var branchId = await _db.Employees.Where(e => e.Id == employeeId)
                    .SelectMany(e => e.Employments)
                    .Where(x => x.IsActive)
                    .OrderByDescending(x => x.ContractStartDate)
                    .Select(x => (int?)x.CompanyProfileId)
                    .FirstOrDefaultAsync();
                var firstAllowed = branchId.HasValue
                    ? await _editLock.GetFirstAllowedDateForContractsAsync(branchId.Value)
                    : null;
                if (firstAllowed.HasValue && folgeMonatErster < firstAllowed.Value)
                    return Conflict(new
                    {
                        error = "LOHN_EDIT_LOCKED",
                        message = $"QST-Kantonswechsel ab {folgeMonatErster:dd.MM.yyyy} liegt in einer verarbeiteten Lohnperiode (frei ab {firstAllowed:dd.MM.yyyy}).",
                        firstAllowedDate = firstAllowed.Value.ToString("yyyy-MM-dd"),
                    });
            }
        }

        // 1) Offenen Eintrag bestätigen: nur Datum + Bemerkung — die Adresse
        //    selbst bleibt exakt wie aus easy@work übernommen.
        offenerWechsel.GueltigAb = umzug;
        offenerWechsel.DatumOffen = false;
        if (!string.IsNullOrWhiteSpace(dto.Bemerkung)) offenerWechsel.Bemerkung = dto.Bemerkung.Trim();

        // 3) QST-Folge-Version bei Kantonswechsel.
        // Dedupe (Walter 08.08.2026): wurde der Wechsel schon über den
        // QST-Tab-Dialog erfasst (Version mit neuem Kanton ab Folgemonat
        // existiert), keine zweite Version anlegen.
        bool qstSchonVersioniert = kantonswechsel && await _db.EmployeeQuellensteuer
            .AnyAsync(q => q.EmployeeId == employeeId
                        && q.ValidFrom == folgeMonatErster
                        && q.Steuerkanton == neuerKanton);
        if (qstSchonVersioniert)
        {
            qstInfo = $"QST-Kantonswechsel war bereits erfasst ({neuerKanton} ab {folgeMonatErster:dd.MM.yyyy}) — keine neue Version.";
        }
        else if (kantonswechsel && qstAlt != null)
        {
            var monatsende = folgeMonatErster.AddDays(-1);
            qstAlt.ValidTo = monatsende;
            qstAlt.UpdatedAt = DateTime.Now;
            var neu = new EmployeeQuellensteuer
            {
                EmployeeId = employeeId,
                ValidFrom = folgeMonatErster,
                ValidTo = null,
                Steuerkanton = neuerKanton,
                SteuerkantonName = KantonName(neuerKanton),
                QstGemeinde = offenerWechsel.Ort,
                QstGemeindeBfsNr = null,
                TarifvorschlagQst = qstAlt.TarifvorschlagQst,
                TarifCode = qstAlt.TarifCode,
                TarifBezeichnung = qstAlt.TarifBezeichnung,
                AnzahlKinder = qstAlt.AnzahlKinder,
                Kirchensteuer = qstAlt.Kirchensteuer,
                QstCode = qstAlt.QstCode,
                SpezielBewilligt = qstAlt.SpezielBewilligt,
                Kategorie = qstAlt.Kategorie,
                Prozentsatz = qstAlt.Prozentsatz,
                MindestlohnSatzbestimmung = qstAlt.MindestlohnSatzbestimmung,
                PartnerEmployeeId = qstAlt.PartnerEmployeeId,
                PartnerEinkommenVon = qstAlt.PartnerEinkommenVon,
                PartnerEinkommenBis = qstAlt.PartnerEinkommenBis,
                ArbeitsortKanton = qstAlt.ArbeitsortKanton,
                WeitereBeschaftigungen = qstAlt.WeitereBeschaftigungen,
                GesamtpensumWeitereAg = qstAlt.GesamtpensumWeitereAg,
                GesamteinkommenWeitereAg = qstAlt.GesamteinkommenWeitereAg,
                Halbfamilie = qstAlt.Halbfamilie,
                WohnsitzAusland = qstAlt.WohnsitzAusland,
                Wohnsitzstaat = qstAlt.Wohnsitzstaat,
                AdresseAusland = qstAlt.AdresseAusland,
                LivesInKonkubinat = qstAlt.LivesInKonkubinat,
                HasJointParentalCare = qstAlt.HasJointParentalCare,
                PaysAlimonyAdultChildren = qstAlt.PaysAlimonyAdultChildren,
                HasHigherIncomeThanPartner = qstAlt.HasHigherIncomeThanPartner,
                IsGrenzgaenger = qstAlt.IsGrenzgaenger,
                IsWochenaufenthalter = qstAlt.IsWochenaufenthalter,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now,
            };
            _db.EmployeeQuellensteuer.Add(neu);
            qstInfo = $"QST: bis {monatsende:dd.MM.yyyy} Kanton {alterKanton}, ab {folgeMonatErster:dd.MM.yyyy} Kanton {neuerKanton} (Tarif {qstAlt.QstCode ?? qstAlt.TarifCode} übernommen — bitte prüfen).";
        }
        else if (kantonswechsel)
        {
            qstInfo = "Kantonswechsel ohne aktive QST-Erfassung — keine QST-Version angelegt.";
        }

        await _db.SaveChangesAsync();
        return Ok(new
        {
            ok = true,
            kantonswechsel,
            qstInfo,
        });
    }

    /// <summary>Historie-Eintrag korrigieren (Admin — v.a. zum Testen/Aufräumen).
    /// Reine Datenkorrektur, KEINE QST-Seiteneffekte.</summary>
    [Authorize(Roles = "admin")]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateEntry(int employeeId, int id, [FromBody] WohnortEntryDto dto)
    {
        var h = await _db.EmployeeWohnortHistories
            .FirstOrDefaultAsync(x => x.Id == id && x.EmployeeId == employeeId);
        if (h == null) return NotFound();
        // BEWUSST keine Adress-Felder (Walter 08.08.2026): PLZ/Ort/Kanton
        // kommen ausschliesslich aus easy@work — hier nur Datum + Bemerkung.
        // GueltigAb: leerer String = «seit jeher» (NULL); fehlend = unverändert.
        if (dto.GueltigAb != null)
            h.GueltigAb = DateOnly.TryParse(dto.GueltigAb, out var ab) ? ab : null;
        if (dto.DatumOffen.HasValue) h.DatumOffen = dto.DatumOffen.Value;
        if (dto.Bemerkung != null)
            h.Bemerkung = string.IsNullOrWhiteSpace(dto.Bemerkung) ? null : dto.Bemerkung.Trim();
        await _db.SaveChangesAsync();
        return Ok(new { h.Id });
    }

    /// <summary>Historie-Eintrag löschen (Admin).</summary>
    [Authorize(Roles = "admin")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteEntry(int employeeId, int id)
    {
        var h = await _db.EmployeeWohnortHistories
            .FirstOrDefaultAsync(x => x.Id == id && x.EmployeeId == employeeId);
        if (h == null) return NotFound();
        _db.EmployeeWohnortHistories.Remove(h);
        await _db.SaveChangesAsync();
        return Ok(new { deleted = true });
    }

    private static string? KantonName(string code) => code switch
    {
        "AG" => "Aargau", "AI" => "Appenzell Innerrhoden", "AR" => "Appenzell Ausserrhoden",
        "BE" => "Bern", "BL" => "Basel-Landschaft", "BS" => "Basel-Stadt", "FR" => "Freiburg",
        "GE" => "Genf", "GL" => "Glarus", "GR" => "Graubünden", "JU" => "Jura",
        "LU" => "Luzern", "NE" => "Neuenburg", "NW" => "Nidwalden", "OW" => "Obwalden",
        "SG" => "St. Gallen", "SH" => "Schaffhausen", "SO" => "Solothurn", "SZ" => "Schwyz",
        "TG" => "Thurgau", "TI" => "Tessin", "UR" => "Uri", "VD" => "Waadt",
        "VS" => "Wallis", "ZG" => "Zug", "ZH" => "Zürich",
        _ => null,
    };
}

public class WohnortEntryDto
{
    /// <summary>ISO yyyy-MM-dd; leerer String = «seit jeher»; null = unverändert.</summary>
    public string? GueltigAb { get; set; }
    public bool? DatumOffen { get; set; }
    public string? Bemerkung { get; set; }
}

public class UmzugDto
{
    public string? Umzugsdatum { get; set; }   // ISO yyyy-MM-dd
    public string? Bemerkung { get; set; }
}
