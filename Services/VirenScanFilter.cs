using System.Security.Claims;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace HrSystem.Services;

/// <summary>
/// Prüft JEDE per Formular hochgeladene Datei, bevor ein Endpunkt sie sieht (Walter-Vorgabe 08.10.2026).
/// Global registriert — neue Upload-Endpunkte sind automatisch abgedeckt. Läuft als Resource-Filter
/// NACH den Authorization-Filtern (Login + RequestFormLimits greifen vorher).
/// Fund → 400 VIRUS_GEFUNDEN + Eintrag in viren_fund. Scanner nicht bereit → 503, Upload abgelehnt.
/// Uploads ausserhalb von Formularen (WebDAV, easy@work-Abruf) prüfen ihre Aufrufer selbst.
/// </summary>
public sealed class VirenScanFilter : IAsyncResourceFilter
{
    private readonly VirenScanner _scanner;
    private readonly AppDbContext _db;
    private readonly ILogger<VirenScanFilter> _log;

    public VirenScanFilter(VirenScanner scanner, AppDbContext db, ILogger<VirenScanFilter> log)
    {
        _scanner = scanner;
        _db = db;
        _log = log;
    }

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var http = context.HttpContext;
        if (!_scanner.Pflicht || !http.Request.HasFormContentType)
        {
            await next();
            return;
        }

        IFormCollection form;
        try
        {
            form = await http.Request.ReadFormAsync(http.RequestAborted);
        }
        catch (Exception)
        {
            // Formular nicht lesbar (z.B. zu gross) — der Endpunkt meldet denselben Fehler selbst.
            await next();
            return;
        }

        foreach (var datei in form.Files)
        {
            if (datei.Length == 0) continue;
            ScanErgebnis erg;
            await using (var s = datei.OpenReadStream())
                erg = await _scanner.PruefeAsync(s, http.RequestAborted);

            if (erg.Status == ScanStatus.NichtBereit)
            {
                context.Result = NichtBereit();
                return;
            }
            if (erg.Status == ScanStatus.Fund)
            {
                int? userId = int.TryParse(http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid) ? uid : null;
                await FundMeldenAsync(_db, VirenFund.QuelleUpload, datei.FileName, erg.Virus!,
                    http.Request.Path.Value ?? "", userId, http.User.Identity?.Name, null, _log);
                context.Result = Fund(datei.FileName, erg.Virus!);
                return;
            }
        }

        await next();
    }

    public static ObjectResult NichtBereit() => new(new
    {
        error = "VIRENSCANNER_NICHT_BEREIT",
        message = "Der Virenscanner ist gerade nicht bereit. Die Datei wurde nicht gespeichert — bitte in ein paar Minuten nochmals versuchen.",
    }) { StatusCode = StatusCodes.Status503ServiceUnavailable };

    public static ObjectResult Fund(string dateiname, string virus) => new(new
    {
        error = "VIRUS_GEFUNDEN",
        message = $"Die Datei «{dateiname}» enthält Schadsoftware ({virus}) und wurde nicht gespeichert. Der Administrator ist informiert.",
    }) { StatusCode = StatusCodes.Status400BadRequest };

    public static async Task FundMeldenAsync(AppDbContext db, string quelle, string dateiname, string virus,
        string ort, int? userId, string? benutzer, int? employeeId, ILogger log)
    {
        log.LogWarning("VIRUS gefunden: {Virus} in «{Datei}» ({Quelle}, {Ort}, Benutzer {Benutzer})",
            virus, dateiname, quelle, ort, benutzer ?? "-");
        try
        {
            db.VirenFunde.Add(new VirenFund
            {
                GefundenAm = DateTime.Now,
                Quelle     = quelle,
                Dateiname  = Kuerzen(dateiname, 500),
                Virus      = Kuerzen(virus, 300),
                Ort        = Kuerzen(ort, 1000),
                UserId     = userId,
                Benutzer   = benutzer is null ? null : Kuerzen(benutzer, 200),
                EmployeeId = employeeId,
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Virenfund konnte nicht gespeichert werden");
        }
    }

    private static string Kuerzen(string s, int max) => s.Length <= max ? s : s[..max];
}
