using System.Security.Claims;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Arbeitszeugnis bei MA-Austritt (Walter-Vorgabe 14.07.2026). Drei
/// Qualitätsstufen (durchschnitt/gut/sehr_gut), Mehrfachauswahl der
/// verrichteten Arbeit (kueche/kasse/drive). PDF read-only — schreibt nichts.
/// GF darf Zeugnisse für seine Filiale erstellen → admin/superuser/user.
/// </summary>
[Authorize(Roles = "admin,superuser,user")]
[ApiController]
[Route("api/arbeitszeugnis")]
public class ArbeitszeugnisController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ArbeitszeugnisPdfService _pdf;

    public ArbeitszeugnisController(AppDbContext db, ArbeitszeugnisPdfService pdf)
    {
        _db = db; _pdf = pdf;
    }

    public class ZeugnisDto
    {
        /// <summary>durchschnitt | gut | sehr_gut</summary>
        public string Qualitaet { get; set; } = "gut";
        /// <summary>kueche | kasse | drive (Mehrfachauswahl)</summary>
        public List<string> Bereiche { get; set; } = new();
        /// <summary>Zeugnis-Datum (Default: heute).</summary>
        public DateOnly? Datum { get; set; }
        /// <summary>«verlässt unser Unternehmen auf eigenen Wunsch» (Default: true).</summary>
        public bool AufEigenenWunsch { get; set; } = true;
        /// <summary>Funktion aus der Vorlage (z.B. «Crew-Trainerin», «Schichtkoordinator»).
        /// Leer = Teilzeit/Vollzeit-Mitarbeiter/in aus dem Vertrag.</summary>
        public string? Funktion { get; set; }
        /// <summary>Explizit gewählte Aufgaben (13er-Katalog der Word-Vorlage, 15.07.2026).
        /// Leer = Ableitung aus den Bereichen.</summary>
        public List<string>? Aufgaben { get; set; }
        /// <summary>true = ZWISCHENzeugnis (Vorlage «289 Hendschiken»).</summary>
        public bool Zwischen { get; set; }
        /// <summary>true = ARBEITSBESTÄTIGUNG (Vorlage «244 Sursee») — nur der
        /// Bestätigungssatz, keine Qualität/Bereiche/Aufgaben nötig.</summary>
        public bool Bestaetigung { get; set; }
        /// <summary>Fiktives Austrittsdatum (Walter 15.07.2026): nur fürs
        /// ARBEITSzeugnis, wenn der LETZTE Vertrag offen ist und kein Austritt
        /// erfasst wurde. Vorschlag im UI: Ende des laufenden Monats.</summary>
        public DateOnly? Austritt { get; set; }
    }

    [HttpPost("{empId:int}/pdf")]
    public async Task<IActionResult> GetPdf(int empId, [FromBody] ZeugnisDto dto)
    {
        var e = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == empId);
        if (e == null) return NotFound(new { error = "EMP_NOT_FOUND" });

        var quali = (dto.Qualitaet ?? "gut").Trim().ToLowerInvariant();
        if (quali is not ("genuegend" or "durchschnitt" or "gut" or "sehr_gut"))
            return BadRequest(new { error = "QUALITAET_UNGUELTIG", message = "Qualität muss genuegend, durchschnitt, gut oder sehr_gut sein." });

        var bereiche = (dto.Bereiche ?? new())
            .Select(b => b.Trim().ToLowerInvariant())
            .Where(b => b is "kueche" or "kasse" or "drive")
            .Distinct().ToList();
        if (bereiche.Count == 0 && !dto.Bestaetigung)
            return BadRequest(new { error = "BEREICH_FEHLT", message = "Mindestens einen Bereich wählen (Küche, Kasse, Drive)." });

        // Verträge: jüngster für Filiale + Pensum, ältester Start + jüngstes Ende
        // für die Beschäftigungsdauer (Fallback: EntryDate/ExitDate am MA).
        var emps = await _db.Employments.AsNoTracking()
            .Where(em => em.EmployeeId == empId)
            .OrderByDescending(em => em.IsActive)
            .ThenByDescending(em => em.ContractStartDate)
            .ToListAsync();
        var last = emps.FirstOrDefault();

        CompanyProfile? cp = null;
        if (last?.CompanyProfileId != null)
            cp = await _db.CompanyProfiles.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == last.CompanyProfileId.Value);
        if (cp == null)
            return BadRequest(new { error = "KEINE_FILIALE", message = "Dem MA ist keine Filiale zugeordnet (kein Vertrag mit Filiale)." });

        var von = e.EntryDate
                  ?? emps.OrderBy(x => x.ContractStartDate).FirstOrDefault()?.ContractStartDate
                  ?? DateTime.Today;
        // Bis-Datum (Walter-Korrektur 15.07.2026): IMMER der LETZTE Vertrag —
        // nicht das juengste Enddatum irgendeines (alten) Vertrags. Ist der
        // letzte Vertrag offen und kein Austritt erfasst, kommt das fiktive
        // Austrittsdatum aus dem Modal (Fallback: Ende laufender Monat).
        var lastByStart = emps.OrderByDescending(x => x.ContractStartDate).FirstOrDefault();
        var monatsEnde = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)
                             .AddMonths(1).AddDays(-1);
        // Walter-Vorgabe 12.08.2026: Das im Modal EINGETRAGENE Austrittsdatum
        // hat IMMER Vorrang (der MA-Austritt ist nur der Vorschlag im Feld).
        // Vorher gewann das Vertragsende über die Eingabe → falsches «bis».
        var bis = (dto.Austritt.HasValue ? dto.Austritt.Value.ToDateTime(TimeOnly.MinValue) : (DateTime?)null)
                  ?? e.ExitDate
                  ?? lastByStart?.ContractEndDate
                  ?? monatsEnde;

        // Vollzeit nur bei FIX/FIX-M mit Pensum ≥ 100 % — Crew/FLEX/MTP = Teilzeit.
        bool vollzeit = last != null
            && (last.EmploymentModel == "FIX" || last.EmploymentModel == "FIX-M")
            && (last.EmploymentPercentage ?? 100m) >= 100m;

        bool female = string.Equals(e.Gender, "female", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(e.Gender, "w", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(e.Gender, "f", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(e.Salutation, "Frau", StringComparison.OrdinalIgnoreCase);

        // Unterschrift + Klarname des EINGELOGGTEN Users (nie eine andere Person).
        byte[]? sigPng = null; string signerName = ""; string? signerTitle = null;
        var idStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (int.TryParse(idStr, out var uid))
        {
            var u = await _db.AppUsers.AsNoTracking()
                .Where(x => x.Id == uid)
                .Select(x => new { x.SignaturePng, x.FirstName, x.LastName, x.Username })
                .FirstOrDefaultAsync();
            if (u != null)
            {
                sigPng = u.SignaturePng;
                var full = $"{u.FirstName} {u.LastName}".Trim();
                signerName = string.IsNullOrWhiteSpace(full) ? (u.Username ?? "") : full;
            }
            // Funktionsbezeichnung aus dem Filial-Zugang (z.B. «Restaurantleiterin»).
            signerTitle = await _db.UserBranchAccesses.AsNoTracking()
                .Where(a => a.UserId == uid && a.CompanyProfileId == cp.Id
                         && a.FunctionTitle != null && a.FunctionTitle != "")
                .Select(a => a.FunctionTitle)
                .FirstOrDefaultAsync();
        }

        var strasse = string.Join(" ", new[] { cp.Street, cp.HouseNumber }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

        var input = new ArbeitszeugnisInput(
            CompanyName:    cp.CompanyName,
            RestaurantName: cp.BranchName ?? cp.FullDisplayName,
            CompanyStreet:  strasse,
            CompanyZipCity: $"{cp.ZipCode} {cp.City}".Trim(),
            CompanyPhone:   cp.Phone,
            CompanyEmail:   cp.Email,
            Ort:            cp.City ?? "",
            Datum:          dto.Datum.HasValue
                                ? dto.Datum.Value.ToDateTime(TimeOnly.MinValue)
                                : DateTime.Today,
            Salutation:     e.Salutation ?? (female ? "Frau" : "Herr"),
            FirstName:      e.FirstName,
            LastName:       e.LastName,
            DateOfBirth:    e.DateOfBirth,
            WohnOrt:        e.City,
            EmpStreet:      e.Street,
            EmpZipCity:     $"{e.ZipCode} {e.City}".Trim(),
            Von:            von,
            Bis:            bis,
            Vollzeit:       vollzeit,
            Female:         female,
            Qualitaet:      quali,
            Bereiche:       bereiche,
            SignatoryName:  signerName,
            SignatoryTitle: signerTitle,
            SignaturePng:   sigPng,
            AufEigenenWunsch: dto.AufEigenenWunsch,
            Funktion:       string.IsNullOrWhiteSpace(dto.Funktion) ? null : dto.Funktion.Trim(),
            Aufgaben:       dto.Aufgaben,
            Zwischen:       dto.Zwischen,
            Bestaetigung:   dto.Bestaetigung
        );

        var bytes = _pdf.Generate(input);
        var art = dto.Bestaetigung ? "Arbeitsbestaetigung" : dto.Zwischen ? "Zwischenzeugnis" : "Arbeitszeugnis";
        return File(bytes, "application/pdf",
            $"{art}_{e.LastName}_{e.FirstName}.pdf".Replace(" ", "_"));
    }
}
