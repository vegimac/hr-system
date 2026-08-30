using HrSystem.Data;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// «Warum dieser Tarif?» — Klartext-Erklärung zur Quellensteuer
/// (Walter-Vorgabe 30.08.2026).
///
/// Bewusst OHNE KI zur Laufzeit und ohne jeden externen Aufruf: die Erklärung
/// hängt nur an Merkmalen (Tarifbuchstabe, Kinderziffer, Kirchensteuer,
/// Zivilstand, Befreiungsgrund). Dieser Controller leitet die zutreffenden
/// Bausteine ab und setzt die Texte aus der Tabelle qst_erklaerung zusammen.
/// Kein Personendatum verlässt das Haus, keine API-Kosten, keine Wartezeit.
/// </summary>
[ApiController]
[Authorize(Roles = "admin,superuser,user")]
[Route("api/qst-erklaerung")]
public class QstErklaerungController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly QstPflichtCheckService _pflicht;

    public QstErklaerungController(AppDbContext db, QstPflichtCheckService pflicht)
    {
        _db = db;
        _pflicht = pflicht;
    }

    public record ErklaerungBaustein(string Code, string Titel, string Text);
    public record ErklaerungResult(string? TarifCode, string Kopfzeile, List<ErklaerungBaustein> Bausteine);

    /// <summary>
    /// GET /api/qst-erklaerung/{employeeId}?entryId=…&amp;sprache=de
    /// Ohne entryId wird die am Stichtag (heute) aktive Erfassung erklärt;
    /// gibt es keine, erklärt die Antwort den Befreiungsgrund.
    /// </summary>
    [HttpGet("{employeeId:int}")]
    public async Task<IActionResult> Get(int employeeId, [FromQuery] int? entryId, [FromQuery] string sprache = "de")
    {
        var emp = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId);
        if (emp == null) return NotFound();

        var heute = DateOnly.FromDateTime(DateTime.Today);
        var erfassung = entryId.HasValue
            ? await _db.EmployeeQuellensteuer.AsNoTracking().FirstOrDefaultAsync(q => q.Id == entryId.Value && q.EmployeeId == employeeId)
            : await _db.EmployeeQuellensteuer.AsNoTracking()
                .Where(q => q.EmployeeId == employeeId && q.ValidFrom <= heute && (q.ValidTo == null || q.ValidTo >= heute))
                .OrderByDescending(q => q.ValidFrom).ThenByDescending(q => q.Id)
                .FirstOrDefaultAsync();

        // ── Zutreffende Bausteine bestimmen ──
        var codes = new List<string>();
        string kopf;
        string? tarifCode = null;

        if (erfassung != null)
        {
            tarifCode = erfassung.TarifCode;
            var buchstabe = (erfassung.TarifCode ?? "").Trim().ToUpperInvariant();
            buchstabe = buchstabe.Length > 0 ? buchstabe[..1] : "";
            kopf = $"Tarif {tarifCode} — so kommt dieser Code zustande.";

            if (buchstabe.Length == 1) codes.Add($"tarif.{buchstabe}");
            codes.Add(erfassung.AnzahlKinder > 0 ? "kinder.n" : "kinder.0");
            codes.Add(erfassung.Kirchensteuer ? "kirche.Y" : "kirche.N");

            var ms = (emp.MaritalStatus ?? "").Trim().ToLowerInvariant();
            if (ms.Contains("getrennt") || emp.SeparatedSince.HasValue) codes.Add("lage.getrennt");

            bool hatKPartner = await _db.EmployeeFamilyMembers.AsNoTracking()
                .AnyAsync(f => f.EmployeeId == employeeId && f.MemberType == "Konkubinatspartner" && f.DateOfDeath == null);
            if (hatKPartner || erfassung.LivesInKonkubinat) codes.Add("lage.konkubinat");

            if (buchstabe == "A" && erfassung.AnzahlKinder > 0) codes.Add("lage.speziell_bewilligt");
            if (erfassung.IsWochenaufenthalter) codes.Add("lage.wochenaufenthalt");
        }
        else
        {
            var check = await _pflicht.CheckAsync(employeeId, heute);
            kopf = check.Message;
            codes.Add(check.BefreiungsGrund switch
            {
                "Ehepartner-CH" or "Ehepartner-C" => "befreiung.ehepartner",
                "CH-Buerger" or "C-Ausweis"       => "befreiung.eigen",
                "Behoerde"                        => "befreiung.behoerde",
                _                                 => "tarif.A"
            });
        }
        codes.Add("hinweis.schluss");

        // ── Texte laden (Fallback immer Deutsch) ──
        var texte = await _db.QstErklaerungen.AsNoTracking()
            .Where(x => codes.Contains(x.Code) && (x.Sprache == sprache || x.Sprache == "de"))
            .ToListAsync();
        var bausteine = codes.Distinct()
            .Select(c => texte.FirstOrDefault(t => t.Code == c && t.Sprache == sprache)
                      ?? texte.FirstOrDefault(t => t.Code == c && t.Sprache == "de"))
            .Where(t => t != null)
            .Select(t => t!)
            .OrderBy(t => t.SortOrder)
            .Select(t => new ErklaerungBaustein(t.Code, t.Titel, t.Text))
            .ToList();

        return Ok(new ErklaerungResult(tarifCode, kopf, bausteine));
    }
}
