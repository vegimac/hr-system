using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Wohnort-Historie + Umzugs-Erfassung (Walter-Vorgabe 07.08.2026).
///
/// «Umzug erfassen» macht in EINEM Schritt:
///   1. History-Eintrag (PLZ/Ort/Kanton, gültig ab Umzugsdatum); existiert
///      noch keine Historie, wird die Bestandsadresse als «seit jeher»
///      zurückgeschrieben, damit das «bis» des Vorgängers ableitbar ist.
///   2. Aktuelle MA-Adresse aktualisieren (PLZ/Ort/Kanton, optional Strasse).
///   3. Bei KANTONSWECHSEL mit aktiver QST-Erfassung: automatische
///      QST-Folge-Version — alter Kanton bis Ende Umzugsmonat, neuer Kanton
///      ab 1. des FOLGEmonats (angebrochener Monat zahlt im alten Kanton).
///      Die neue Version übernimmt Tarif/Kinder/Kirchensteuer unverändert.
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
        if (string.IsNullOrWhiteSpace(dto.Plz) || string.IsNullOrWhiteSpace(dto.Ort)
            || string.IsNullOrWhiteSpace(dto.Kanton))
            return BadRequest(new { error = "PLZ_ORT_KANTON_PFLICHT" });

        var neuerKanton = dto.Kanton.Trim().ToUpperInvariant();
        var alterKanton = (emp.CantonCode ?? "").Trim().ToUpperInvariant();
        bool kantonswechsel = !string.IsNullOrEmpty(alterKanton) && alterKanton != neuerKanton;

        // QST-Folge-Version vorbereiten (VOR dem Schreiben prüfen, damit der
        // Umzug bei Lock komplett abbricht statt halb erfasst zu sein).
        var folgeMonatErster = new DateOnly(umzug.Year, umzug.Month, 1).AddMonths(1);
        EmployeeQuellensteuer? qstAlt = null;
        string? qstInfo = null;
        if (kantonswechsel)
        {
            qstAlt = await _db.EmployeeQuellensteuer
                .Where(q => q.EmployeeId == employeeId
                         && q.ValidFrom < folgeMonatErster
                         && (q.ValidTo == null || q.ValidTo >= umzug))
                .OrderByDescending(q => q.ValidFrom)
                .FirstOrDefaultAsync();
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

        // 1) Historie: initialen Bestand sichern, dann neuen Eintrag.
        bool hatHistorie = await _db.EmployeeWohnortHistories
            .AnyAsync(h => h.EmployeeId == employeeId);
        if (!hatHistorie)
        {
            _db.EmployeeWohnortHistories.Add(new EmployeeWohnortHistory
            {
                EmployeeId = employeeId,
                Plz = emp.ZipCode, Ort = emp.City, KantonCode = emp.CantonCode,
                GueltigAb = null,
                Bemerkung = "Bestandsadresse (automatisch beim ersten Umzug)",
            });
        }
        _db.EmployeeWohnortHistories.Add(new EmployeeWohnortHistory
        {
            EmployeeId = employeeId,
            Plz = dto.Plz.Trim(), Ort = dto.Ort.Trim(), KantonCode = neuerKanton,
            GueltigAb = umzug,
            Bemerkung = string.IsNullOrWhiteSpace(dto.Bemerkung) ? null : dto.Bemerkung.Trim(),
        });

        // 2) Aktuelle Adresse nachziehen.
        emp.ZipCode = dto.Plz.Trim();
        emp.City = dto.Ort.Trim();
        emp.CantonCode = neuerKanton;
        if (!string.IsNullOrWhiteSpace(dto.Strasse)) emp.Street = dto.Strasse.Trim();

        // 3) QST-Folge-Version bei Kantonswechsel.
        if (kantonswechsel && qstAlt != null)
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
                QstGemeinde = dto.Ort.Trim(),
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

public class UmzugDto
{
    public string? Umzugsdatum { get; set; }   // ISO yyyy-MM-dd
    public string? Plz { get; set; }
    public string? Ort { get; set; }
    public string? Kanton { get; set; }
    public string? Strasse { get; set; }       // optional: neue Strasse
    public string? Bemerkung { get; set; }
}
